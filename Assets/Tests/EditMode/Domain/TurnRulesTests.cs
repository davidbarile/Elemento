using Elemento.Domain;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class TurnRulesTests
    {
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void GetNextPlayerIndex_SwitchesBetweenTwoPlayers(int inCurrentPlayerIndex, int inExpectedPlayerIndex)
        {
            Assert.That(TurnRules.GetNextPlayerIndex(inCurrentPlayerIndex, 2), Is.EqualTo(inExpectedPlayerIndex));
        }

        [Test]
        public void IsHumanSeat_AgainstAIOnlyFirstSeatIsHuman()
        {
            Assert.That(TurnRules.IsHumanSeat(EGameMode.AgainstAI, 0), Is.True);
            Assert.That(TurnRules.IsHumanSeat(EGameMode.AgainstAI, 1), Is.False);
        }

        [Test]
        public void IsHumanSeat_PassAndPlayMakesBothSeatsHuman()
        {
            Assert.That(TurnRules.IsHumanSeat(EGameMode.PassAndPlay, 0), Is.True);
            Assert.That(TurnRules.IsHumanSeat(EGameMode.PassAndPlay, 1), Is.True);
        }

        [Test]
        public void RequiresHandoff_OnlyWhenPassAndPlayChangesSeats()
        {
            Assert.That(TurnRules.RequiresHandoff(EGameMode.PassAndPlay, 0, 1), Is.True);
            Assert.That(TurnRules.RequiresHandoff(EGameMode.PassAndPlay, -1, 0), Is.False);
            Assert.That(TurnRules.RequiresHandoff(EGameMode.PassAndPlay, 0, 0), Is.False);
            Assert.That(TurnRules.RequiresHandoff(EGameMode.AgainstAI, 0, 1), Is.False);
        }

        [Test]
        public void IsHandVisible_PassAndPlayOnlyShowsCurrentViewerHand()
        {
            Assert.That(TurnRules.IsHandVisible(EGameMode.PassAndPlay, 1, 1, false), Is.True);
            Assert.That(TurnRules.IsHandVisible(EGameMode.PassAndPlay, 0, 1, false), Is.False);
        }

        [Test]
        public void IsHandVisible_PassAndPlayHidesEveryHandDuringHandoff()
        {
            Assert.That(TurnRules.IsHandVisible(EGameMode.PassAndPlay, 0, 0, true), Is.False);
            Assert.That(TurnRules.IsHandVisible(EGameMode.PassAndPlay, 1, 1, true), Is.False);
        }
    }
}
