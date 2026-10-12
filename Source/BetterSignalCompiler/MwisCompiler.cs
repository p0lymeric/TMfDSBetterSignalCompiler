// Compiler implementation based off a maximum-weight independent set (MWIS) solver for weighted interval graphs
// similar to https://en.wikipedia.org/wiki/Interval_scheduling#Weighted
// polymeric 2026

// Some limitations:
// - Currently no support for expressing bare IDs (|<ID> / @<ID>_UNDEF).
//   If we support this we should check what happens if the final puzzle receives an answer of |10.
// - It is assumed that the game case-normalizes the input string and dictionary prior to invoking the compiler.
//   Our tokenization routine performs exact character matching, instead of case-insensitive matching.
// - The prefix trie is rebuilt on every compile, rather than incrementally modified on dictionary edits.
// - The compiler is capable of tracking error spans, and line/column numbers can be trivially computed.
//   However, we do not display this information.
// - Ambiguities are currently reported as "Entry not found" with 2 traces delimited by a bar (e.g. TIMID ITY  |  TIMI DITY).
//   LogError/CompileErrorType can be extended to add an Ambiguous error category.

namespace polymeric.BetterSignalCompiler;

public enum TokenizeResultKind {
    Word,
    Numeral,
    // BareId,
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

    // For use with BinarySearch.
    public static readonly Comparer<TokenizeResult> PrecedesPartitionPointComparer = Comparer<TokenizeResult>.Create((x, y) => x.end <= y.start ? -1 : 1);
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
    public static SignalMessage CompileStringToSignal(string input, ref CompilerResult cr) {
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal input: {input}");

        if (input.Trim() == "") {
            cr.compilerResultTag = CompilerResultTag.ERROR;
            cr.LogError(input, CompileErrorType.EmptyEntry);
            Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: EmptyEntry");
        }

        Trie<int> trie = new();
        foreach (var (id, word) in UserDictionary.Instance.terms) {
            trie.Insert(id, word);
        }
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal trie: {trie}");

        List<TokenizeResult> token_candidates = [];
        for (int i = 0; i < input.Length; i++) {
            foreach (TriePrefix<int> result in trie.Prefixes(input, i)) {
                token_candidates.Add(new TokenizeResult(TokenizeResultKind.Word, result.id, i, i + result.length));
            }
            // Free in the land of the UTF-16, where digit chars are guaranteed to be contiguous and ordered...
            if (input[i] >= '0' && input[i] <= '9') {
                token_candidates.Add(new TokenizeResult(TokenizeResultKind.Numeral, input[i] - '0', i, i + 1));
            }
        }
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal token_candidates: {string.Join(", ", token_candidates)}");

        token_candidates.Sort((x, y) => (x.end, x.start).CompareTo((y.end, y.start)));

        MwisWeight[] weights = new MwisWeight[token_candidates.Count + 1];
        int[] precedes = new int[token_candidates.Count + 1];
        MwisWeight[] optimal = new MwisWeight[token_candidates.Count + 1];

        weights[0] = new MwisWeight(0, 0);
        precedes[0] = 0;
        optimal[0] = new MwisWeight(0, 0);

        for (int i = 1; i <= token_candidates.Count; i++) {
            TokenizeResult tk = token_candidates[i - 1];
            uint num_non_whitespace = 0;
            foreach (char c in input[tk.start..tk.end]) {
                if (!char.IsWhiteSpace(c)) {
                    num_non_whitespace++;
                }
            }
            weights[i] = new MwisWeight(num_non_whitespace, 1);
            precedes[i] = ~token_candidates.BinarySearch(0, i - 1, tk, TokenizeResult.PrecedesPartitionPointComparer);

            MwisWeight left = weights[i] + optimal[precedes[i]];
            MwisWeight right = optimal[i - 1];
            optimal[i] = left.CompareTo(right) > 0 ? left : right;
        }
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal weights: {string.Join<MwisWeight>(", ", weights)}");
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal precedes: {string.Join(", ", precedes)}");
        // Plugin.Singleton.logger.LogDebug($"CompileStringToSignal optimal: {string.Join<MwisWeight>(", ", optimal)}");

        List<int> opt_schedule = [];
        List<int> ambiguous_points = [];
        int j = token_candidates.Count;
        while (j > 0) {
            int compare = (weights[j] + optimal[precedes[j]]).CompareTo(optimal[j - 1]);
            if (compare == 0) {
                ambiguous_points.Add(j);
            }
            if (compare >= 0) {
                opt_schedule.Add(j);
                j = precedes[j];
            } else {
                j--;
            }
        }
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal opt_schedule: {string.Join(", ", opt_schedule)}");
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal ambiguous_points: {string.Join(", ", ambiguous_points)}");

        int[] signals = new int[opt_schedule.Count];
        for (int i = 0; i < opt_schedule.Count; i++) {
            signals[opt_schedule.Count - i - 1] = token_candidates[opt_schedule[i] - 1].id;
        }
        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal signals: {string.Join(", ", signals)}");

        void report_uncovered_non_whitespace(ref CompilerResult cr, int end_prev, int start) {
            int? error_start = null;
            for(int i = end_prev; i < start; i++) {
                char c = input[i];
                if (!char.IsWhiteSpace(c)) {
                    error_start ??= i;
                } else {
                    if (error_start.HasValue) {
                        cr.compilerResultTag = CompilerResultTag.ERROR;
                        cr.LogError(input[error_start.GetValueOrDefault()..i], CompileErrorType.EntryNotFound);
                        Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: EntryNotFound {input[error_start.GetValueOrDefault()..i]}");
                        error_start = null;
                    }
                }
            }
            if (error_start.HasValue) {
                cr.compilerResultTag = CompilerResultTag.ERROR;
                cr.LogError(input[error_start.GetValueOrDefault()..start], CompileErrorType.EntryNotFound);
                Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: EntryNotFound {input[error_start.GetValueOrDefault()..start]}");
            }
        }
        int end_prev = 0;
        for (int i = opt_schedule.Count - 1; i >= 0; i--) {
            TokenizeResult tk = token_candidates[opt_schedule[i] - 1];
            report_uncovered_non_whitespace(ref cr, end_prev, tk.start);
            end_prev = tk.end;
        }
        if (end_prev < input.Length) {
            report_uncovered_non_whitespace(ref cr, end_prev, input.Length);
        }

        foreach (int i in ambiguous_points) {
            List<TokenizeResult> left_divergence = [];
            List<TokenizeResult> right_divergence = [];

            int j_left = precedes[i];
            TokenizeResult tk_left = token_candidates[i - 1];
            left_divergence.Add(tk_left);

            int j_right = i - 1;

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

            string error_string = "";
            error_string += string.Join(" ", left_divergence.Select(tk => input[tk.start..tk.end]));
            error_string += "  |  ";
            error_string += string.Join(" ", right_divergence.Select(tk => input[tk.start..tk.end]));

            cr.compilerResultTag = CompilerResultTag.ERROR;
		    cr.LogError(error_string, CompileErrorType.EntryNotFound);
            Plugin.Singleton.logger.LogDebug($"CompileStringToSignal cr.LogError: Ambiguous {error_string}");
        }

        return new SignalMessage { signals = signals };
    }
}
