using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class BoardStateManager : MonoBehaviour
{
    public static BoardStateManager IN;
    public static bool IsDoingReplay;
    public static bool IsSettingState;

    /*
    [Button("$ButtonName", ButtonSizes.Large), GUIColor(0, 1, 0)]
    public void SetGameToMoveButton()
    {
        for (int i = 0; i < this.BoardHistory.BoardStates.Count; i++)
        {
            var state = this.BoardHistory.BoardStates[i];

            if (state.ShouldSet)
            {
                state.ShouldSet = false;
                this.ButtonName = $"Set Game to Turn: {state.TurnNumber}, Move:  {i}, Player: {state.ActivePlayer}";
                GameManager.IN.SetGameToBoardState(state, this.ShouldPlayFromGameState);
                return;
            }
        }
    }

    [HideInInspector] public string ButtonName = "Set Game to Move";

    public bool ShouldPlayFromGameState;
    */

    public BoardState CurrentReplayBoardState;

    public BoardTurn BoardHistory;
    public List<BoardTurn> BoardTurns;

    public void ClearBoardStates()
    {
        this.BoardHistory.BoardStates.Clear();
        this.BoardTurns.Clear();
    }

    public void AddBoardState(BoardState inBoardState)
    {
        this.BoardHistory.BoardStates.Add(inBoardState);

        if (this.BoardTurns.Count < GameManager.IN.CurrentTurnNumber)
            this.BoardTurns.Add(new BoardTurn());

        if (GameManager.IN.CurrentTurnNumber > 0 && this.BoardTurns.Count > GameManager.IN.CurrentTurnNumber - 1)
            this.BoardTurns[GameManager.IN.CurrentTurnNumber - 1].BoardStates.Add(inBoardState);
        else
            Debug.Log($"<color=red>Error!  AddBoardState()   GameManager.IN.CurrentTurnNumber is 0</color>");
    }
}