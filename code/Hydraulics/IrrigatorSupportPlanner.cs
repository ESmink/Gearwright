using System;
using System.Collections.Generic;
using System.Linq;

namespace Gearwright.Hydraulics;

/// <summary>Pure deterministic support-spacing rules shared by runtime and tests.</summary>
public static class IrrigatorSupportPlanner
{
    public const int NegativeSupport = 1;
    public const int PositiveSupport = 2;
    public const int MaximumUnsupportedPipes = 3;

    public static bool TryPlan(
        int count,
        IReadOnlyList<bool> eligible,
        out int[] masks,
        out int breakIndex)
    {
        masks = new int[Math.Max(0, count)];
        breakIndex = 0;
        if (count <= 0 || eligible.Count < count) return false;
        if (count == 1)
        {
            if (!eligible[0]) return false;
            masks[0] = NegativeSupport | PositiveSupport;
            return true;
        }

        int first = -1;
        for (int i = 0; i <= Math.Min(MaximumUnsupportedPipes, count - 1); i++)
        {
            if (!eligible[i]) continue;
            first = i;
            break;
        }
        if (first < 0)
        {
            breakIndex = 0;
            return false;
        }

        List<int> supports = new() { first };
        int last = first;
        while (count - 1 - last > MaximumUnsupportedPipes)
        {
            int farthest = Math.Min(last + MaximumUnsupportedPipes + 1, count - 1);
            int next = -1;
            for (int i = farthest; i > last; i--)
            {
                if (!eligible[i]) continue;
                next = i;
                break;
            }
            if (next < 0)
            {
                breakIndex = farthest;
                return false;
            }
            supports.Add(next);
            last = next;
        }

        if (eligible[0] && !supports.Contains(0)) supports.Insert(0, 0);
        if (eligible[count - 1] && !supports.Contains(count - 1)) supports.Add(count - 1);

        foreach (int index in supports.Distinct())
        {
            masks[index] = index == count - 1 ? PositiveSupport : NegativeSupport;
        }
        return true;
    }
}
