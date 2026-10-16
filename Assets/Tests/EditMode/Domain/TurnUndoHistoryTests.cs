using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class TurnUndoHistoryTests
    {
        [Test]
        public void History_PopsMostRecentStateFirst()
        {
            var history = new TurnUndoHistory<string>();
            history.BeginTurn(ETurnUndoCheckpoint.Draw);
            history.Record("first");
            history.Record("second");

            Assert.That(history.TryPop(out string second, out _), Is.True);
            Assert.That(history.TryPop(out string first, out _), Is.True);
            Assert.That(second, Is.EqualTo("second"));
            Assert.That(first, Is.EqualTo("first"));
        }

        [Test]
        public void Checkpoint_DropsEarlierStatesAndReportsBarrier()
        {
            var history = new TurnUndoHistory<int>();
            history.BeginTurn(ETurnUndoCheckpoint.Draw);
            history.Record(1);

            history.SetCheckpoint(ETurnUndoCheckpoint.RevealHiddenCard);

            Assert.That(history.TryPop(out _, out ETurnUndoCheckpoint blockedBy), Is.False);
            Assert.That(blockedBy, Is.EqualTo(ETurnUndoCheckpoint.RevealHiddenCard));
        }

        [Test]
        public void ActivateDeckCheckpoint_AllowsLaterUndoThenReportsActivationBarrier()
        {
            var history = new TurnUndoHistory<string>();
            history.BeginTurn(ETurnUndoCheckpoint.Draw);
            history.Record("before activation");
            history.SetCheckpoint(ETurnUndoCheckpoint.ActivateDeck);
            history.Record("after activation");

            Assert.That(history.TryPop(out string snapshot, out _), Is.True);
            Assert.That(snapshot, Is.EqualTo("after activation"));
            Assert.That(history.TryPop(out _, out ETurnUndoCheckpoint blockedBy), Is.False);
            Assert.That(blockedBy, Is.EqualTo(ETurnUndoCheckpoint.ActivateDeck));
        }

        [Test]
        public void LockedTurn_CannotRecordOrUndo()
        {
            var history = new TurnUndoHistory<int>();
            history.BeginTurn(ETurnUndoCheckpoint.Draw);
            history.Record(1);

            history.LockTurn();
            history.Record(2);

            Assert.That(history.Count, Is.Zero);
            Assert.That(history.TryPop(out _, out ETurnUndoCheckpoint blockedBy), Is.False);
            Assert.That(blockedBy, Is.EqualTo(ETurnUndoCheckpoint.EndTurn));
        }
    }
}
