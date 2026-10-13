using System.Globalization;
using System.Text.RegularExpressions;

// Compiler implementation based off a maximum-weight independent set (MWIS) solver for weighted interval graphs
// similar to https://en.wikipedia.org/wiki/Interval_scheduling#Weighted
// polymeric 2026

// Some limitations:
// - Bare ID parsing (|<number>) is supported. Note that solving the final transmission with |10 will advance
//   currPuzz/currPuzzListID/currPuzzLocalID past the number of puzzles and cause future game launches to crash
//  (until the save file is edited to revert those fields).
// - Our tokenization routine performs exact character matching, instead of case-insensitive matching.
//   It is assumed that the game case-normalizes the input string and dictionary prior to invoking the compiler.
// - The prefix trie is rebuilt on every compile, rather than incrementally modified on dictionary edits.
// - The compiler is capable of tracking error spans, and line/column numbers can be trivially computed.
//   However, we do not display this information.
// - The compiler's error display does not scroll, which limits the amount of text that can be presented as feedback.
//   Additionally, the error display clears after a brief time, which limits the amount of text that can be reasonably
//   read (without screenshotting the game)

namespace polymeric.BetterSignalCompiler;

public enum TokenizeResultKind {
    Word,
    Numeral,
    BareId,
    BareIdBig,
}

public class TokenizeResult {
    public readonly TokenizeResultKind kind;
    public readonly int id;
    public readonly int start;
    public readonly int end;

    public TokenizeResult(TokenizeResultKind kind, int id, int start, int end) {
        this.kind = kind;
        this.id = id;
        this.start = start;
        this.end = end;
    }

    public static int PartitionPointEndLtEqNeedle(List<TokenizeResult> list, int first, int last, int needle) {
        // Based on implementation sketched in https://en.cppreference.com/cpp/algorithm/partition_point
        int length = last - first;
        while (length > 0) {
            int half = length / 2;
            int middle = first + half;
            if (list[middle].end <= needle) {
                first = middle + 1;
                length -= half + 1;
            } else {
                length = half;
            }
        }
        return first;
    }
}

public class MwisWeight : IComparable<MwisWeight> {
    public readonly uint non_whitespace_chars;
    public readonly uint tokens_penalty;

    public MwisWeight(uint non_whitespace_chars, uint tokens_penalty) {
        this.non_whitespace_chars = non_whitespace_chars;
        this.tokens_penalty = tokens_penalty;
    }

    public int CompareTo(MwisWeight other) {
        return (this.non_whitespace_chars, other.tokens_penalty).CompareTo((other.non_whitespace_chars, this.tokens_penalty));
    }

    public static MwisWeight operator +(MwisWeight x, MwisWeight y) {
        return new MwisWeight(x.non_whitespace_chars + y.non_whitespace_chars, x.tokens_penalty + y.tokens_penalty);
    }
}

public static class MwisCompiler {
    private static readonly Regex BareIdRegex = new(@"\G\|(-?[0-9]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SignalMessage CompileStringToSignal(string input, ref CompilerResult cr) {
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal input: {input}");

        // Report EmptyEntry on an empty or all-whitespace input and return
        if (input.Trim() == "") {
            cr.compilerResultTag = CompilerResultTag.ERROR;
            cr.LogError(input, CompileErrorType.EmptyEntry);
            Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: EmptyEntry");
            return new SignalMessage { signals = [] };
        }

        // Iterate over the player's dictionary and build a prefix trie
        // This also includes unnamed entries that have been assigned an @<ID>_UNDEF placeholder name
        Trie<int> trie = new();
        foreach (var (id, word) in UserDictionary.Instance.terms) {
            trie.Insert(id, word);
        }
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal trie: {trie}");

        // Collect all possible tokens that match words in the dictionary, digits and special string patterns
        List<TokenizeResult> token_candidates = [];
        for (int i = 0; i < input.Length; i++) {
            // Tokenize based on the dictionary
            foreach (TriePrefix<int> result in trie.Prefixes(input, i)) {
                token_candidates.Add(new TokenizeResult(TokenizeResultKind.Word, result.id, i, i + result.length));
            }
            // Tokenize individual digits
            if (input[i] >= '0' && input[i] <= '9') {
                token_candidates.Add(new TokenizeResult(TokenizeResultKind.Numeral, input[i] - '0', i, i + 1));
            }
            // Tokenize bare IDs
            if (Plugin.Singleton.configParseBareIds.Value) {
                // TODO prune tokens that overlap with bare IDs
                // (there is no problem with priority as bare IDs are wider than digits and the dictionary does not allow | characters)
                Match bare_id_match = BareIdRegex.Match(input, i);
                if (bare_id_match.Success) {
                    string digits_string = bare_id_match.Groups[1].Value;
                    bool digits_parsed = int.TryParse(digits_string, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int digits_int);
                    if (digits_parsed) {
                        token_candidates.Add(new TokenizeResult(TokenizeResultKind.BareId, digits_int, i, i + bare_id_match.Length));
                    } else {
                        token_candidates.Add(new TokenizeResult(TokenizeResultKind.BareIdBig, 0, i, i + bare_id_match.Length));
                    }
                }
            }
        }
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal token_candidates: {string.Join(", ", token_candidates)}");

        // Sort by token end to satisfy a prerequisite of the optimization algorithm. Start is used as a tiebreaker to help with determinism.
        token_candidates.Sort((x, y) => (x.end, x.start).CompareTo((y.end, y.start)));

        // BEGIN the weighted interval scheduling problem solver
        // weights, precedes, and optimal are known as w, p and OPT/M in literature
        MwisWeight[] weights = new MwisWeight[token_candidates.Count + 1];
        int[] precedes = new int[token_candidates.Count + 1];
        MwisWeight[] optimal = new MwisWeight[token_candidates.Count + 1];

        // Index 0 stores a base case for the solver
        // Indices referring to elements in token_candidates are offset by 1.
        weights[0] = new MwisWeight(0, 0);
        precedes[0] = 0;
        optimal[0] = new MwisWeight(0, 0);

        // Look officer, I know this looks bad, but I swear these arrays don't actually begin at 1!
        for (int i = 1; i <= token_candidates.Count; i++) {
            // Populate weights based on our definition of MwisWeight
            TokenizeResult tk = token_candidates[i - 1];
            uint num_non_whitespace = 0;
            foreach (char c in input[tk.start..tk.end]) {
                if (!char.IsWhiteSpace(c)) {
                    num_non_whitespace++;
                }
            }
            weights[i] = new MwisWeight(num_non_whitespace, 1);

            // Count the number of tokens that end before this token starts. token_candidates is sorted by end, so find by binary search.
            precedes[i] = TokenizeResult.PartitionPointEndLtEqNeedle(token_candidates, 0, i - 1, tk.start);

            // Memoize the optimal total weight if a solution were to end at that token
            MwisWeight left = weights[i] + optimal[precedes[i]];
            MwisWeight right = optimal[i - 1];
            optimal[i] = left.CompareTo(right) > 0 ? left : right;
        }
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal weights: {string.Join<MwisWeight>(", ", weights)}");
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal precedes: {string.Join(", ", precedes)}");
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal optimal: {string.Join<MwisWeight>(", ", optimal)}");

        // Construct a solution, while checking if the solution is unique.
        List<int> opt_schedule = [];
        List<int> ambiguous_points = [];
        int j = token_candidates.Count;
        while (j > 0) {
            int compare = (weights[j] + optimal[precedes[j]]).CompareTo(optimal[j - 1]);
            if (compare == 0) {
                // Solution is not unique. Push the index so we can trace it later.
                ambiguous_points.Add(j);
            }
            if (compare >= 0) {
                opt_schedule.Add(j);
                j = precedes[j];
            } else {
                j--;
            }
        }
        opt_schedule.Reverse();
        ambiguous_points.Reverse();
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal opt_schedule: {string.Join(", ", opt_schedule)}");
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal ambiguous_points: {string.Join(", ", ambiguous_points)}");
        // END the weighted interval scheduling problem solver

        // Iterate through the solution tokens and the uncovered spans around the tokens
        SortedDictionary<(int, int), string> traces = [];
        int[] signals = new int[opt_schedule.Count];
        int end_prev = 0;
        for (int i = 0; i < opt_schedule.Count; i++) {
            TokenizeResult tk = token_candidates[opt_schedule[i] - 1];
            if (tk.kind != TokenizeResultKind.BareIdBig) {
                // Copy solution token IDs to signals & add them to the trace
                signals[i] = tk.id;
                traces.Add((tk.start, tk.end), $"|{tk.id}");
            } else {
                // Unless they were bare IDs that failed to parse into an int
                traces.Add((tk.start, tk.end), $"|TOOBIG:(");
                cr.compilerResultTag = CompilerResultTag.ERROR;
                cr.LogError(input[tk.start..tk.end], CompileErrorType.NaN);
                Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: NaN {input[tk.start..tk.end]}");
            }

            // Verify that the span to the next token only contains whitespace
            // If it does not then report a compile error and record it in the trace
            CheckUncoveredNonWhitespace(ref cr, traces, input, end_prev, tk.start);
            end_prev = tk.end;
        }
        if (end_prev < input.Length) {
            CheckUncoveredNonWhitespace(ref cr, traces, input, end_prev, input.Length);
        }
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal signals: {string.Join(", ", signals)}");

        // Iff the solution is unique, then ambiguous_points will be empty
        // But if not, extract more information on the ambiguitie(s)
        foreach (int i in ambiguous_points) {
            // left_divergence is the path taken by the solution in opt_schedule
            List<TokenizeResult> left_divergence = [];
            // right_divergence is the path taken if we made the opposite choice specifically at index i
            List<TokenizeResult> right_divergence = [];

            int j_left = precedes[i];
            TokenizeResult tk_left = token_candidates[i - 1];
            left_divergence.Add(tk_left);

            int j_right = i - 1;

            // Now that we seeded the different choice, we construct the rest of the paths by using the
            // same algorithm as before, where any further ambiguities down the two paths are handled by
            // picking "precedes[j]" over "j - 1".

            // In other words, we don't generate all possible optimal solutions, just two per point to
            // illustrate the different parses to the player.

            // We alternate building the two paths until they converge on the same j.
            while (j_left != j_right) {
                if (j_left > j_right) {
                    int compare = (weights[j_left] + optimal[precedes[j_left]]).CompareTo(optimal[j_left - 1]);
                    if(compare >= 0) {
                        TokenizeResult tk = token_candidates[j_left - 1];
                        left_divergence.Add(tk);
                        j_left = precedes[j_left];
                    } else {
                        j_left--;
                    }
                } else {
                    int compare = (weights[j_right] + optimal[precedes[j_right]]).CompareTo(optimal[j_right - 1]);
                    if(compare >= 0) {
                        TokenizeResult tk = token_candidates[j_right - 1];
                        right_divergence.Add(tk);
                        j_right = precedes[j_right];
                    } else {
                        j_right--;
                    }
                }
            }

            left_divergence.Reverse();
            right_divergence.Reverse();

            // for span reporting
            // int start = 0;
            // if (right_divergence.Count > 0) {
            //     start = Math.Min(left_divergence[0].start, right_divergence[0].start);
            // } else {
            //     start = left_divergence[0].start;
            // }

            // left_divergence is a subset of the path in opt_schedule. Find the corresponding record in traces and mark them with a question mark.
            for (int k = 0; k < left_divergence.Count; k++) {
                TokenizeResult tkl = left_divergence[k];
                if (left_divergence.Count == right_divergence.Count) {
                    // If left and right divergence are the same size (they should be because the two solutions we explored have the same overall token count),
                    // zip through the left and right paths and append "?<right id at same idx>" to the trace
                    traces[(tkl.start, tkl.end)] = $"|{tkl.id}?{right_divergence[k].id}";
                } else {
                    // Maybe there is an edge case where the two paths don't have equal length if the solution isn't valid (not sure)
                    // If that is so, just append "?" to the left path's IDs
                    traces[(tkl.start, tkl.end)] = $"|{tkl.id}?";
                }
            }

            // Report a compile error
            string error_string = "";
            error_string += string.Join(" ", left_divergence.Select(tk => input[tk.start..tk.end]));
            error_string += "  ?  ";
            error_string += string.Join(" ", right_divergence.Select(tk => input[tk.start..tk.end]));
            cr.compilerResultTag = CompilerResultTag.ERROR;
		    cr.LogError(error_string, (CompileErrorType)HarmonyPatches.Patch__CompilerResult__GetStringFromErrorType.CompileErrorTypeExtended.Ambiguous);
            Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: Ambiguous {error_string}");
        }

        // If a compile error was ever reported, display the partial compile trace to the player
        string trace_string = string.Join(" ", traces.Select(pair => pair.Value));
        if (cr.compilerResultTag == CompilerResultTag.ERROR) {
            cr.LogError(trace_string, (CompileErrorType)HarmonyPatches.Patch__CompilerResult__GetStringFromErrorType.CompileErrorTypeExtended.PartialCompile);
        }
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal trace: {trace_string}");

        return new SignalMessage { signals = signals };
    }

    private static void CheckUncoveredNonWhitespace(ref CompilerResult cr, SortedDictionary<(int, int), string> traces, string input, int end_prev, int start) {
        int? error_start = null;
        for(int i = end_prev; i < start; i++) {
            char c = input[i];
            if (!char.IsWhiteSpace(c)) {
                error_start ??= i;
            } else {
                if (error_start.HasValue) {
                    cr.compilerResultTag = CompilerResultTag.ERROR;
                    string missing = input[error_start.GetValueOrDefault()..i];
                    cr.LogError(missing, CompileErrorType.EntryNotFound);
                    Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: EntryNotFound {missing}");
                    traces.Add((error_start.GetValueOrDefault(), i), missing);
                    error_start = null;
                }
            }
        }
        if (error_start.HasValue) {
            cr.compilerResultTag = CompilerResultTag.ERROR;
            string missing = input[error_start.GetValueOrDefault()..start];
            cr.LogError(missing, CompileErrorType.EntryNotFound);
            Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: EntryNotFound {missing}");
            traces.Add((error_start.GetValueOrDefault(), start), missing);
        }
    }
}
