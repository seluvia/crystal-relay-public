using System.Collections.ObjectModel;
using CrystalRelayLiveList.ViewModels;

namespace CrystalRelayLiveList.Services;

public sealed record LiveListDiff<T>(
    IReadOnlyList<T> ToAdd,
    IReadOnlyList<T> ToRemove,
    IReadOnlyList<T> ToUpdate,
    IReadOnlySet<string> UnchangedKeys);

public static class LiveListDiffer
{
    public static LiveListDiff<T> Diff<T>(
        ObservableCollection<T> current,
        IReadOnlyList<T> incoming,
        Func<T, string> keySelector,
        Func<T, T, bool> equals,
        IEqualityComparer<string>? keyComparer = null)
    {
        var cmp = keyComparer ?? StringComparer.OrdinalIgnoreCase;
        var currentByKey = new Dictionary<string, (int Index, T Item)>(cmp);
        for (var i = 0; i < current.Count; i++)
        {
            currentByKey[keySelector(current[i])] = (i, current[i]);
        }

        var incomingKeys = new HashSet<string>(cmp);
        var toAdd = new List<T>();
        var toUpdate = new List<T>();
        var unchanged = new HashSet<string>(cmp);

        foreach (var item in incoming)
        {
            var key = keySelector(item);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!incomingKeys.Add(key))
            {
                continue;
            }

            if (currentByKey.TryGetValue(key, out var existing))
            {
                if (equals(existing.Item, item))
                {
                    unchanged.Add(key);
                }
                else
                {
                    toUpdate.Add(item);
                }
            }
            else
            {
                toAdd.Add(item);
            }
        }

        var toRemove = new List<T>();
        foreach (var (key, existing) in currentByKey)
        {
            if (!incomingKeys.Contains(key))
            {
                toRemove.Add(existing.Item);
            }
        }

        return new LiveListDiff<T>(toAdd, toRemove, toUpdate, unchanged);
    }

    public static void Apply<T>(
        ObservableCollection<T> current,
        LiveListDiff<T> diff,
        Func<T, string> keySelector,
        Action<T, T>? replaceInPlace = null,
        IEqualityComparer<string>? keyComparer = null)
    {
        // Remove first (stable).
        foreach (var item in diff.ToRemove)
        {
            for (var i = current.Count - 1; i >= 0; i--)
            {
                if (Equals(current[i], item))
                {
                    current.RemoveAt(i);
                    break;
                }
            }
        }

        // Update in place where supported; otherwise replace by key.
        var cmp = keyComparer ?? StringComparer.OrdinalIgnoreCase;
        for (var i = 0; i < current.Count; i++)
        {
            var key = keySelector(current[i]);
            foreach (var item in diff.ToUpdate)
            {
                if (cmp.Equals(key, keySelector(item)))
                {
                    if (replaceInPlace is not null)
                    {
                        replaceInPlace(current[i], item);
                    }
                    else
                    {
                        current[i] = item;
                    }
                    break;
                }
            }
        }

        // Add.
        foreach (var item in diff.ToAdd)
        {
            current.Add(item);
        }
    }
}
