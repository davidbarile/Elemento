using System;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class SupabaseResponseTests
    {
        [Test]
        public void SubmitTurn_ParsesResultWithAdditionalServerFields()
        {
            var result = NetJson.Deserialize<SubmitTurnResultData>("{\"Accepted\":false,\"Reason\":\"stale_revision\",\"NewRevision\":4,\"CurrentSeat\":1,\"Status\":\"active\",\"Extra\":null}");
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.NewRevision, Is.EqualTo(4));
            Assert.That(result.Reason, Is.EqualTo("stale_revision"));
        }

        [Test]
        public void SubmitTurn_ParsesAcceptedAndRejectedResponses()
        {
            var accepted = SubmitTurnResponseData.Parse("{\"Accepted\":true,\"NewRevision\":4,\"CurrentSeat\":1,\"Status\":\"active\"}");
            var rejected = SubmitTurnResponseData.Parse("{\"Accepted\":false,\"Reason\":\"stale_revision\",\"NewRevision\":4,\"CurrentSeat\":1,\"Status\":\"active\"}");

            Assert.That(accepted.Accepted, Is.True);
            Assert.That(accepted.NewRevision, Is.EqualTo(4));
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.Reason, Is.EqualTo("stale_revision"));
            Assert.That(accepted.CurrentSeat, Is.EqualTo(1));
            Assert.That(accepted.Status, Is.EqualTo("active"));
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
        public void InvalidAuthSession_IsRejected()
        {
            Assert.Throws<FormatException>(() => SupabaseSessionData.Parse("{\"access_token\":\"access\"}"));
        }
    }
}
