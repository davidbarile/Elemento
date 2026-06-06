using System;
using System.Collections.Generic;

[Serializable]
public class Deck
{
    public string Name;
    public string ID;
    public bool IsDefault;
    public List<int> CardCounts = new();

    public static Deck Clone(Deck inSource, int inCount = -1, bool inIsCopy = false)
    {
        var name = inIsCopy ? $"{inSource.Name} (copy)" : inSource.Name;

        Deck clone = new Deck();
        clone.ID = UnityEngine.Random.Range(9,9999999).ToString();
        clone.Name = name;

        var cardCounts = inSource.CardCounts;

        if(inCount > -1)
        {
            cardCounts = new();

            for (int i = 0; i < inSource.CardCounts.Count; i++)
            {
                cardCounts.Add(inCount);
            }
        }

        clone.CardCounts = new List<int>(cardCounts);

        return clone;
    }

    public int GetTotalCardsCount()
    {
        int totalCount = 0;

        foreach (var count in this.CardCounts)
        {
            totalCount += count;
        }

        return totalCount;
    }
}