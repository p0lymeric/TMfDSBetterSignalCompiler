// Trie for whole-dictionary prefix matching
// polymeric 2026

namespace polymeric.BetterSignalCompiler;

class TrieNode<T> {
    public Dictionary<char, TrieNode<T>> children = [];
    public List<T> ids = [];
}

public class TriePrefix<T> {
    public readonly T id;
    public readonly int length;

    public TriePrefix(T id, int length) {
        this.id = id;
        this.length = length;
    }
}

public class Trie<T> where T : IComparable<T> {
    readonly TrieNode<T> root = new();

    public bool Insert(T id, string word) {
        TrieNode<T> node = this.root;
        foreach (char c in word) {
            if (node.children.TryGetValue(c, out TrieNode<T> out_next_node)) {
                node = out_next_node;
            } else {
                TrieNode<T> new_next_node = new();
                node.children[c] = new_next_node;
                node = new_next_node;
            }
        }
        int index = node.ids.BinarySearch(id);
        if (index < 0) {
            node.ids.Insert(~index, id);
            return true;
        }
        return false;
    }

    public IReadOnlyList<T> Search(string word) {
        TrieNode<T> node = this.root;
        foreach (char c in word) {
            TrieNode<T>? next_node = node.children.GetValueOrDefault(c);
            if (next_node != null) {
                node = next_node;
            } else {
                return [];
            }
        }
        return node.ids;
    }

    public bool Delete(T id, string word) {
        TrieNode<T> node = this.root;
        TrieNode<T>? maybeYoungestCaWithCousinNode = null;
        char maybeYoungestCaWithCousinChar = '\0';

        if (word.Length > 0){
            maybeYoungestCaWithCousinNode = this.root;
            maybeYoungestCaWithCousinChar = word[0];
        }

        foreach (char c in word) {
            TrieNode<T>? next_node = node.children.GetValueOrDefault(c);
            if (next_node != null) {
                if (node.ids.Count > 0 || node.children.Count > 1) {
                    maybeYoungestCaWithCousinNode = node;
                    maybeYoungestCaWithCousinChar = c;
                }
                node = next_node;
            } else {
                return false;
            }
        }

        int index_to_delete = node.ids.BinarySearch(id);
        if (index_to_delete >= 0 ) {
            if (maybeYoungestCaWithCousinNode != null && node.ids.Count == 1 && node.children.Count == 0) {
                maybeYoungestCaWithCousinNode.children.Remove(maybeYoungestCaWithCousinChar);
            } else {
                node.ids.RemoveAt(index_to_delete);
            }
            return true;
        } else {
            return false;
        }
    }

    public List<TriePrefix<T>> Prefixes(string text, int offset) {
        List<TriePrefix<T>> result = [];
        TrieNode<T> node = this.root;
        foreach(T id in node.ids) {
            result.Add(new TriePrefix<T>(id, 0));
        }
        for (int i = offset; i < text.Length; i++) {
            char c = text[i];
            TrieNode<T>? next_node = node.children.GetValueOrDefault(c);
            if (next_node != null) {
                node = next_node;
                foreach(T id in node.ids) {
                    result.Add(new TriePrefix<T>(id, i - offset + 1));
                }
            } else {
                break;
            }
        }
        return result;
    }
}
