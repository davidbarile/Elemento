using System;
using System.Collections.Generic;

namespace Elemento.Domain
{
    public static class CardIdentityValidator
    {
        public static bool TryValidate(IEnumerable<string> inExpectedIds, int inExpectedAnonymousCount,
            IEnumerable<string> inActualIds, int inActualAnonymousCount, out string outError)
        {
            if (inExpectedAnonymousCount < 0)
                throw new ArgumentOutOfRangeException(nameof(inExpectedAnonymousCount));
            if (inActualAnonymousCount < 0)
                throw new ArgumentOutOfRangeException(nameof(inActualAnonymousCount));

            List<string> expectedIds = CopyIds(inExpectedIds);
            List<string> actualIds = CopyIds(inActualIds);
            if (TryFindInvalidId(expectedIds, "snapshot", out outError)
                || TryFindInvalidId(actualIds, "restored state", out outError))
            {
                return false;
            }

            if (expectedIds.Count + inExpectedAnonymousCount != actualIds.Count + inActualAnonymousCount)
            {
                outError = $"total count changed from {expectedIds.Count + inExpectedAnonymousCount} "
                    + $"to {actualIds.Count + inActualAnonymousCount}.";
                return false;
            }

            if (inExpectedAnonymousCount != inActualAnonymousCount)
            {
                outError = $"anonymous reserve count changed from {inExpectedAnonymousCount} "
                    + $"to {inActualAnonymousCount}.";
                return false;
            }

            var expectedSet = new HashSet<string>(expectedIds, StringComparer.Ordinal);
            var actualSet = new HashSet<string>(actualIds, StringComparer.Ordinal);
            if (!expectedSet.SetEquals(actualSet))
            {
                outError = "card id set changed. Missing: " + JoinDifference(expectedSet, actualSet)
                    + "; unexpected: " + JoinDifference(actualSet, expectedSet) + ".";
                return false;
            }

            outError = string.Empty;
            return true;
        }

        private static List<string> CopyIds(IEnumerable<string> inIds)
        {
            return inIds == null ? new List<string>() : new List<string>(inIds);
        }

        private static bool TryFindInvalidId(List<string> inIds, string inSource, out string outError)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in inIds)
            {
                if (string.IsNullOrEmpty(id))
                {
                    outError = inSource + " contains a card without an id.";
                    return true;
                }
                if (!seen.Add(id))
                {
                    outError = inSource + " contains duplicate card id '" + id + "'.";
                    return true;
                }
            }

            outError = string.Empty;
            return false;
        }

        private static string JoinDifference(HashSet<string> inFirst, HashSet<string> inSecond)
        {
            List<string> difference = new();
            foreach (string id in inFirst)
            {
                if (!inSecond.Contains(id))
                    difference.Add(id);
            }
            return difference.Count == 0 ? "none" : string.Join(", ", difference);
        }
    }
}
