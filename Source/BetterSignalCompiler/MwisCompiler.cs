using System.Globalization;
using System.Text.RegularExpressions;

// Compiler implementation based off a maximum-weight independent set (MWIS) solver for weighted interval graphs
// similar to https://en.wikipedia.org/wiki/Interval_scheduling#Weighted
// polymeric 2026

// Some limitations:
// - Bare ID parsing (|<number>) is supported. Note that solving the final transmission with |10 will advance
//   currPuzz/currPuzzListID/currPuzzLocalID past the number of puzzles and cause future game launches to crash
//   (until the save file is edited to revert those fields).
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
    public readonly uint nonWhitespaceChars;
    public readonly uint tokensPenalty;

    public MwisWeight(uint nonWhitespaceChars, uint tokensPenalty) {
        this.nonWhitespaceChars = nonWhitespaceChars;
        this.tokensPenalty = tokensPenalty;
    }

    public int CompareTo(MwisWeight other) {
        return (this.nonWhitespaceChars, other.tokensPenalty).CompareTo((other.nonWhitespaceChars, this.tokensPenalty));
    }

    public static MwisWeight operator +(MwisWeight x, MwisWeight y) {
        return new MwisWeight(x.nonWhitespaceChars + y.nonWhitespaceChars, x.tokensPenalty + y.tokensPenalty);
    }
}

public enum CompileErrorTypeInternal {
    NaN,
    EntryNotFound,
    EmptyEntry,
    Ambiguous,
    BareIdIntegerTooWide,
}

public static class CompileErrorTypeInternalExtensions {
    public static CompileErrorType MapToCompileErrorType(this CompileErrorTypeInternal ceti) {
        return ceti switch {
            CompileErrorTypeInternal.NaN => CompileErrorType.NaN,
            CompileErrorTypeInternal.EntryNotFound => CompileErrorType.EntryNotFound,
            CompileErrorTypeInternal.EmptyEntry => CompileErrorType.EmptyEntry,
            CompileErrorTypeInternal.Ambiguous => CompileErrorType.EntryNotFound,
            CompileErrorTypeInternal.BareIdIntegerTooWide => CompileErrorType.NaN,
            _ => CompileErrorType.NaN,
        };
    }

    public static string GetString(this CompileErrorTypeInternal ceti) {
        return ceti switch {
            CompileErrorTypeInternal.NaN => "Not a Number",
            CompileErrorTypeInternal.EntryNotFound => "Entry not found",
            CompileErrorTypeInternal.EmptyEntry => "Null Input",
            CompileErrorTypeInternal.Ambiguous => "Ambiguous",
            CompileErrorTypeInternal.BareIdIntegerTooWide => "ID out of bounds",
            _ => "Miscellaneous Error",
        };
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
        List<TokenizeResult> tokenCandidates = [];
        for (int i = 0; i < input.Length; i++) {
            // Tokenize based on the dictionary
            foreach (TriePrefix<int> result in trie.Prefixes(input, i)) {
                tokenCandidates.Add(new TokenizeResult(TokenizeResultKind.Word, result.id, i, i + result.length));
            }
            // Tokenize individual digits
            if (input[i] >= '0' && input[i] <= '9') {
                tokenCandidates.Add(new TokenizeResult(TokenizeResultKind.Numeral, input[i] - '0', i, i + 1));
            }
            // Tokenize bare IDs
            if (Plugin.Singleton.configParseBareIds.Value) {
                // TODO prune tokens that overlap with bare IDs
                // (there is no problem with priority as bare IDs are wider than digits and the dictionary does not allow | characters)
                Match bareIdMatch = BareIdRegex.Match(input, i);
                if (bareIdMatch.Success) {
                    string digitsString = bareIdMatch.Groups[1].Value;
                    bool digitsParsed = int.TryParse(digitsString, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int digitsInt);
                    if (digitsParsed) {
                        tokenCandidates.Add(new TokenizeResult(TokenizeResultKind.BareId, digitsInt, i, i + bareIdMatch.Length));
                    } else {
                        tokenCandidates.Add(new TokenizeResult(TokenizeResultKind.BareIdBig, 0, i, i + bareIdMatch.Length));
                    }
                }
            }
        }
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal tokenCandidates: {string.Join(", ", tokenCandidates)}");

        // Sort by token end to satisfy a prerequisite of the optimization algorithm. Start is used as a tiebreaker to help with determinism.
        tokenCandidates.Sort((x, y) => (x.end, x.start).CompareTo((y.end, y.start)));

        // BEGIN the weighted interval scheduling problem solver
        // weights, precedes, and optimal are known as w, p and OPT/M in literature
        MwisWeight[] weights = new MwisWeight[tokenCandidates.Count + 1];
        int[] precedes = new int[tokenCandidates.Count + 1];
        MwisWeight[] optimal = new MwisWeight[tokenCandidates.Count + 1];

        // Index 0 stores a base case for the solver
        // Indices referring to elements in tokenCandidates are offset by 1.
        weights[0] = new MwisWeight(0, 0);
        precedes[0] = 0;
        optimal[0] = new MwisWeight(0, 0);

        // Look officer, I know this looks bad, but I swear these arrays don't actually begin at 1!
        for (int i = 1; i <= tokenCandidates.Count; i++) {
            // Populate weights based on our definition of MwisWeight
            TokenizeResult tk = tokenCandidates[i - 1];
            uint numNonWhitespace = 0;
            foreach (char c in input[tk.start..tk.end]) {
                if (!char.IsWhiteSpace(c)) {
                    numNonWhitespace++;
                }
            }
            weights[i] = new MwisWeight(numNonWhitespace, 1);

            // Count the number of tokens that end before this token starts. tokenCandidates is sorted by end, so find by binary search.
            precedes[i] = TokenizeResult.PartitionPointEndLtEqNeedle(tokenCandidates, 0, i - 1, tk.start);

            // Memoize the optimal total weight if a solution were to end at that token
            MwisWeight left = weights[i] + optimal[precedes[i]];
            MwisWeight right = optimal[i - 1];
            optimal[i] = left.CompareTo(right) > 0 ? left : right;
        }
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal weights: {string.Join<MwisWeight>(", ", weights)}");
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal precedes: {string.Join(", ", precedes)}");
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal optimal: {string.Join<MwisWeight>(", ", optimal)}");

        // Construct a solution, while checking if the solution is unique.
        List<int> optSchedule = [];
        List<int> ambiguousPoints = [];
        int j = tokenCandidates.Count;
        while (j > 0) {
            int compare = (weights[j] + optimal[precedes[j]]).CompareTo(optimal[j - 1]);
            if (compare == 0) {
                // Solution is not unique. Push the index so we can trace it later.
                ambiguousPoints.Add(j);
            }
            if (compare >= 0) {
                optSchedule.Add(j);
                j = precedes[j];
            } else {
                j--;
            }
        }
        optSchedule.Reverse();
        ambiguousPoints.Reverse();
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal optSchedule: {string.Join(", ", optSchedule)}");
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal ambiguousPoints: {string.Join(", ", ambiguousPoints)}");
        // END the weighted interval scheduling problem solver

        // Now, we build 3 arrays:
        // signals - the IDs of all the tokens we could parse, to be returned in SignalMessage
        // errors - a list of (string, CompileErrorTypeInternal) to be reported to cr
        // traces - a merged list of signal IDs and errors that provide a summary of how the input string was tokenized
        int[] signals = new int[optSchedule.Count];
        List<(string, CompileErrorTypeInternal)> errors = [];
        SortedDictionary<(int, int), string> traces = [];

        // Iterate through the solution tokens and the uncovered spans around the tokens
        int endPrev = 0;
        for (int i = 0; i < optSchedule.Count; i++) {
            TokenizeResult tk = tokenCandidates[optSchedule[i] - 1];
            if (tk.kind != TokenizeResultKind.BareIdBig) {
                // Copy solution token IDs to signals & add them to the trace
                signals[i] = tk.id;
                traces.Add((tk.start, tk.end), $"|{tk.id}");
            } else {
                // Unless they were bare IDs that failed to parse into an int
                errors.Add((input[tk.start..tk.end], CompileErrorTypeInternal.BareIdIntegerTooWide));
                traces.Add((tk.start, tk.end), $"|TOOBIG:(");
            }

            // Verify that the span to the next token only contains whitespace
            // If it does not then report a compile error and record it in the trace
            CheckUncoveredNonWhitespace(errors, traces, input, endPrev, tk.start);
            endPrev = tk.end;
        }
        if (endPrev < input.Length) {
            CheckUncoveredNonWhitespace(errors, traces, input, endPrev, input.Length);
        }
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal signals: {string.Join(", ", signals)}");

        // Iff the solution is unique, then ambiguousPoints will be empty
        // But if not, extract more information on the ambiguitie(s)
        foreach (int i in ambiguousPoints) {
            // leftDivergence is the path taken by the solution in optSchedule
            List<TokenizeResult> leftDivergence = [];
            // rightDivergence is the path taken if we made the opposite choice specifically at index i
            List<TokenizeResult> rightDivergence = [];

            int jLeft = precedes[i];
            TokenizeResult tkLeft = tokenCandidates[i - 1];
            leftDivergence.Add(tkLeft);

            int jRight = i - 1;

            // Now that we seeded the different choice, we construct the rest of the paths by using the
            // same algorithm as before, where any further ambiguities down the two paths are handled by
            // picking "precedes[j]" over "j - 1".

            // In other words, we don't generate all possible optimal solutions, just two per point to
            // illustrate the different parses to the player.

            // We alternate building the two paths until they converge on the same j.
            while (jLeft != jRight) {
                if (jLeft > jRight) {
                    int compare = (weights[jLeft] + optimal[precedes[jLeft]]).CompareTo(optimal[jLeft - 1]);
                    if(compare >= 0) {
                        TokenizeResult tk = tokenCandidates[jLeft - 1];
                        leftDivergence.Add(tk);
                        jLeft = precedes[jLeft];
                    } else {
                        jLeft--;
                    }
                } else {
                    int compare = (weights[jRight] + optimal[precedes[jRight]]).CompareTo(optimal[jRight - 1]);
                    if(compare >= 0) {
                        TokenizeResult tk = tokenCandidates[jRight - 1];
                        rightDivergence.Add(tk);
                        jRight = precedes[jRight];
                    } else {
                        jRight--;
                    }
                }
            }

            leftDivergence.Reverse();
            rightDivergence.Reverse();

            // for span reporting
            // int start = 0;
            // if (rightDivergence.Count > 0) {
            //     start = Math.Min(leftDivergence[0].start, rightDivergence[0].start);
            // } else {
            //     start = leftDivergence[0].start;
            // }

            // leftDivergence is a subset of the path in optSchedule. Find the corresponding record in traces and mark them with a question mark.
            for (int k = 0; k < leftDivergence.Count; k++) {
                TokenizeResult tkl = leftDivergence[k];
                if (leftDivergence.Count == rightDivergence.Count) {
                    // If left and right divergence are the same size (they should be because the two solutions we explored have the same overall token count),
                    // zip through the left and right paths and append "?<right id at same idx>" to the trace
                    traces[(tkl.start, tkl.end)] = $"|{tkl.id}?{rightDivergence[k].id}";
                } else {
                    // Maybe there is an edge case where the two paths don't have equal length if the solution isn't valid (not sure)
                    // If that is so, just append "?" to the left path's IDs
                    traces[(tkl.start, tkl.end)] = $"|{tkl.id}?";
                }
            }

            // Report a compile error
            string errorString = "";
            errorString += string.Join(" ", leftDivergence.Select(tk => input[tk.start..tk.end]));
            errorString += "  ?  ";
            errorString += string.Join(" ", rightDivergence.Select(tk => input[tk.start..tk.end]));
            errors.Add((errorString, CompileErrorTypeInternal.Ambiguous));
        }

        // Concatenate the partial compile trace string
        string traceString = string.Join(" ", traces.Select(pair => pair.Value));
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal trace: {traceString}");

        // If a compile error was ever reported, assemble the error message
        if (errors.Count > 0) {
            cr.compilerResultTag = CompilerResultTag.ERROR;
            ref string errorMsg = ref DelegatesAndRefs.CompilerResult__errorMsg(cr);
            errorMsg = "- Compilation Failed -" + Environment.NewLine;
            // Trace
            ConfigErrorFormat cef = Plugin.Singleton.configErrorFormat.Value;
            if (cef == ConfigErrorFormat.TraceAndCount || cef == ConfigErrorFormat.TraceAndVerbose) {
                errorMsg += traceString + Environment.NewLine + Environment.NewLine;
            }
            // Count
            if (cef == ConfigErrorFormat.TraceAndCount) {
                foreach (var group in errors.GroupBy(err => err.Item2).OrderBy(grp => grp.Key)) {
                    errorMsg += $"{group.Key.GetString()}: {group.Count()}" + Environment.NewLine;
                }
            }
            // Verbose
            if (cef == ConfigErrorFormat.Verbose || cef == ConfigErrorFormat.TraceAndVerbose) {
                int errorIdx = 1;
                foreach (var (errorPortion, ceti) in errors) {
                    errorMsg += $"{errorIdx} - {ceti.GetString()}: " + Environment.NewLine + errorPortion + Environment.NewLine;
                    errorIdx++;
                }
            }
            // Vanilla
            // {
            //     foreach (var (errorPortion, ceti) in errors) {
            //         cr.LogError(errorMsg, ceti.MapToCompileErrorType());
            //     }
            // }
            Plugin.Singleton.logger.LogDebug($"CompileStringToSignal errorMsg: {errorMsg}");
        }

        return new SignalMessage { signals = signals };
    }

    private static void CheckUncoveredNonWhitespace(List<(string, CompileErrorTypeInternal)> errors, SortedDictionary<(int, int), string> traces, string input, int endPrev, int start) {
        int? errorStart = null;
        for(int i = endPrev; i < start; i++) {
            char c = input[i];
            if (!char.IsWhiteSpace(c)) {
                errorStart ??= i;
            } else {
                if (errorStart.HasValue) {
                    string missing = input[errorStart.GetValueOrDefault()..i];
                    traces.Add((errorStart.GetValueOrDefault(), i), missing);
                    errors.Add((missing, CompileErrorTypeInternal.EntryNotFound));
                    errorStart = null;
                }
            }
        }
        if (errorStart.HasValue) {
            string missing = input[errorStart.GetValueOrDefault()..start];
            traces.Add((errorStart.GetValueOrDefault(), start), missing);
            errors.Add((missing, CompileErrorTypeInternal.EntryNotFound));
        }
    }
}
