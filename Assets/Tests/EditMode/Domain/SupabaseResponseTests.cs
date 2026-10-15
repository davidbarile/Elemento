using System;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class SupabaseResponseTests
    {
        [Test]
        public void CreateMatch_ParsesNestedMetadataAndInviteCode()
        {
            var result = SupabaseMatchResponseData.Parse("{\"match\":{\"id\":\"match-1\",\"invite_code\":\"ABC234\",\"mode\":\"live\",\"status\":\"waiting\",\"revision\":0},\"seat\":0}");
            Assert.That(result.Match.Id, Is.EqualTo("match-1"));
            Assert.That(result.Match.InviteCode, Is.EqualTo("ABC234"));
            Assert.That(result.Seat, Is.Zero);
            Assert.That(result.View, Is.Null);
        }

        [Test]
        public void WaitingGetView_ParsesExplicitNullView()
        {
            var result = SupabaseMatchResponseData.Parse("{\"match\":{\"id\":\"match-1\",\"status\":\"waiting\",\"revision\":0},\"view\":null}");

            Assert.That(result.Match.Status, Is.EqualTo("waiting"));
            Assert.That(result.View, Is.Null);
        }

        [Test]
        public void JoinAndGetView_KeepFairViewEnvelopeAndSeat()
        {
            const string view = "{\"ProtocolVersion\":1,\"MatchId\":\"match-1\",\"Seat\":1,\"View\":{\"Revision\":12,\"CurrentPlayerId\":1}}";
            foreach (string seatField in new[] { ",\"seat\":1", "" })
            {
                var result = SupabaseMatchResponseData.Parse("{\"match\":{\"id\":\"match-1\",\"revision\":12}" + seatField + ",\"view\":" + view + "}");
                Assert.That(result.View.Seat, Is.EqualTo(1));
                Assert.That(result.View.View.Revision, Is.EqualTo(12));
            }
        }

        [Test]
        public void SubmitMove_ParsesResultWithAdditionalServerView()
        {
            var result = NetJson.Deserialize<MoveResultData>("{\"ProtocolVersion\":1,\"Accepted\":false,\"Reason\":\"Revision mismatch.\",\"NewRevision\":4,\"LogEntry\":null,\"View\":{\"Seat\":1}}");
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.NewRevision, Is.EqualTo(4));
            Assert.That(result.Reason, Is.EqualTo("Revision mismatch."));
        }

        [Test]
        public void SubmitTurn_ParsesAcceptedAndRejectedResponses()
        {
            const string view = "{\"MatchId\":\"match-1\",\"Seat\":1,\"View\":{\"Revision\":4}}";
            var accepted = SubmitTurnResponseData.Parse("{\"Accepted\":true,\"NewRevision\":4,\"MoveCount\":3,\"View\":" + view + "}");
            var rejected = SubmitTurnResponseData.Parse("{\"Accepted\":false,\"Reason\":\"illegal\",\"FailedIndex\":1,\"NewRevision\":4,\"MoveCount\":0,\"View\":" + view + "}");
            var rejectedWithoutIndex = SubmitTurnResponseData.Parse("{\"Accepted\":false,\"FailedIndex\":null,\"NewRevision\":4,\"MoveCount\":0,\"View\":" + view + "}");

            Assert.That(accepted.Accepted, Is.True);
            Assert.That(accepted.MoveCount, Is.EqualTo(3));
            Assert.That(rejected.FailedIndex, Is.EqualTo(1));
            Assert.That(rejectedWithoutIndex.FailedIndex, Is.Null);
        }

        [Test]
        public void GetView_ParsesOrderedMoveRowsAndReplayCard()
        {
            const string json = "{\"match\":{\"id\":\"match-1\",\"status\":\"active\",\"revision\":3},\"view\":{\"MatchId\":\"match-1\",\"Seat\":1,\"View\":{\"Revision\":3}},\"moves\":[{\"revision\":2,\"seat\":0,\"command\":{\"MatchId\":\"match-1\",\"Seat\":0,\"ClientMoveSequence\":1,\"ExpectedRevision\":1,\"ClientNonce\":\"turn-a\",\"Move\":{\"PlayerId\":0,\"Type\":2,\"CardId\":\"bomb-1\"},\"ReplayCard\":{\"Id\":\"bomb-1\",\"DefinitionId\":7,\"Bombs\":[0,0,3,0]}}},{\"revision\":3,\"seat\":0,\"command\":{\"MatchId\":\"match-1\",\"Seat\":0,\"ClientMoveSequence\":2,\"ExpectedRevision\":2,\"ClientNonce\":\"turn-a\",\"Move\":{\"PlayerId\":0,\"Type\":5}}}]}";
            var update = SupabaseMatchResponseData.Parse(json).ToViewUpdate();

            Assert.That(update.Moves, Has.Count.EqualTo(2));
            Assert.That(update.Moves[0].RevisionBefore, Is.EqualTo(1));
            Assert.That(update.Moves[0].RevisionAfter, Is.EqualTo(2));
            Assert.That(update.Moves[0].Move.CardId, Is.EqualTo("bomb-1"));
            Assert.That(update.Moves[0].ReplayCard.DefinitionId, Is.EqualTo(7));
            Assert.That(update.Moves[0].ReplayCard.Bombs[2], Is.EqualTo(3));
            Assert.That(update.Moves[1].RevisionAfter, Is.EqualTo(3));
        }

        [Test]
        public void AuthSession_RoundTripsSnakeCaseAndRetainsIdentity()
        {
            var result = SupabaseSessionData.Parse("{\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"expires_at\":2100000000,\"expires_in\":3600,\"user\":{\"id\":\"user-1\"}}");
            string json = NetJson.Serialize(result);
            StringAssert.Contains("\"access_token\":\"access\"", json);
            Assert.That(SupabaseSessionData.Parse(json).UserId, Is.EqualTo("user-1"));
            Assert.That(result.ExpiresAt, Is.EqualTo(2100000000));
        }

        [Test]
        public void InvalidServerEnvelopes_AreRejected()
        {
            Assert.Throws<FormatException>(() => SupabaseMatchResponseData.Parse("{\"error\":\"missing profile\"}"));
            Assert.Throws<FormatException>(() => SupabaseSessionData.Parse("{\"access_token\":\"access\"}"));
            Assert.Throws<FormatException>(() => SupabaseMatchResponseData.Parse("{\"match\":{\"id\":\"one\"},\"view\":{\"MatchId\":\"two\",\"View\":{}}}"));
            Assert.Throws<FormatException>(() => SupabaseMatchResponseData.Parse("{\"match\":{\"id\":\"one\",\"status\":\"active\"},\"view\":null}"));
        }
    }
}
