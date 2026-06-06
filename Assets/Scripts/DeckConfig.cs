using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Deck", menuName = "ScriptableObjects/Deck Config", order = 2)]
public class DeckConfig : ScriptableObject
{
    public string DeckName;
    public string DeckID;
    public bool IsDefaultDeck;
    public List<int> CardCounts;
}