using System;
using System.Collections.Generic;
using System.IO;
using DVSurvival.Core;
using DVSurvival.Multiplayer;
using Newtonsoft.Json;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class TravelProvisionsTests
    {
        [Fact]
        public void CatalogueNeverReusesRetiredAlcoholId()
        {
            Assert.False(ProvisionCatalog.IsSupported((ProvisionKind)6));
            Assert.False(PartialProvisionLedger.Supports((ProvisionKind)6));
            for (int i = 0; i < ProvisionCatalog.Count; i++)
            {
                var kind = ProvisionCatalog.At(i);
                Assert.NotEqual((ProvisionKind)6, kind);
                Assert.Equal(i, ProvisionCatalog.ArtworkIndex(kind));
                Assert.True(ProvisionCatalog.IsSupported(kind));
            }
        }
        [Theory]
        [InlineData(ProvisionKind.CannedFood, 60f, 0f)]
        [InlineData(ProvisionKind.Tea, 0f, 35f)]
        public void NewItemsSupportPartialConsumptionAndPersistentRemainders(ProvisionKind kind, float food, float water)
        {
            var portions = new Dictionary<string, PartialProvisionRecord>(); var consumed = new HashSet<string>();
            string identity = Guid.NewGuid().ToString("D");
            var state = new SurvivalState { Hunger = 10, Hydration = 10, Rest = 20, LowRestGameHours = 80 };
            Assert.Equal(SurvivalResultCode.Success, PartialProvisionLedger.Consume(portions, consumed,
                identity, 400, state, kind, out var used));
            Assert.Equal(400, used); Assert.Equal(10 + food * .4f, state.Hunger, 3);
            Assert.Equal(10 + water * .4f, state.Hydration, 3);
            portions = JsonConvert.DeserializeObject<Dictionary<string, PartialProvisionRecord>>(JsonConvert.SerializeObject(portions));
            Assert.Equal(SurvivalResultCode.Success, PartialProvisionLedger.Consume(portions, consumed,
                identity, 400, state, kind, out used));
            Assert.Equal(10 + food * .4f, state.Hunger, 3); // Retransmission does not grant another portion.
            Assert.Equal(SurvivalResultCode.Success, PartialProvisionLedger.Consume(portions, consumed,
                identity, 1000, state, kind, out used));
            Assert.Equal(1000, used); Assert.Equal(10 + food, state.Hunger, 3);
            Assert.Equal(10 + water, state.Hydration, 3); Assert.Single(consumed); Assert.Empty(portions);
            Assert.Equal(20, state.Rest); Assert.Equal(80, state.LowRestGameHours);
            Assert.Equal(0, state.CoffeeUsesSinceSleep); Assert.Equal(0, state.CaffeineHours);
        }

        [Theory]
        [InlineData(36.95f, 37f)] [InlineData(34f, 34.15f)] [InlineData(39f, 39f)]
        public void TeaWarmsOnlyBelowNormalAndDoesNotTreatSleepDeprivation(float before, float after)
        {
            var state = new SurvivalState { Hydration = 10, BodyTemperatureCelsius = before, Rest = 10, LowRestGameHours = 80 };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.ConsumePhysical(state, ProvisionKind.Tea, new SurvivalTuning()));
            Assert.Equal(after, state.BodyTemperatureCelsius, 3); Assert.Equal(45, state.Hydration);
            Assert.Equal(10, state.Rest); Assert.Equal(80, state.LowRestGameHours);
        }

        [Fact]
        public void NewPricesSurviveFullAndCompactPacketsAndUpdateConfiguration()
        {
            var sender = new StateConfigurationCache(); var receiver = new StateConfigurationCache();
            var full = new SurvivalStateMessage { SessionId = "host", CannedFoodPrice = 125, TeaPrice = 55 };
            Assert.True(sender.Prepare(full, true)); full = RoundTrip(full); Assert.True(receiver.Apply(full));
            Assert.Equal(125, full.GetPrice(ProvisionKind.CannedFood)); Assert.Equal(55, full.GetPrice(ProvisionKind.Tea));
            var compact = new SurvivalStateMessage { SessionId = "host", CannedFoodPrice = 125, TeaPrice = 55 };
            Assert.False(sender.Prepare(compact, false)); compact = RoundTrip(compact);
            Assert.True(receiver.Apply(compact)); Assert.Equal(125, compact.CannedFoodPrice); Assert.Equal(55, compact.TeaPrice);
            var changed = new SurvivalStateMessage { SessionId = "host", CannedFoodPrice = 130, TeaPrice = 60 };
            Assert.True(sender.Prepare(changed, false)); Assert.Equal(2u, changed.ConfigurationRevision);
            Assert.True(receiver.Apply(RoundTrip(changed)));
        }

        [Theory]
        [InlineData(ProvisionKind.CannedFood, 125)] [InlineData(ProvisionKind.Tea, 55)]
        public void NewStockHasCareerPriceAndIsFreeInSandbox(ProvisionKind kind, int price)
        {
            Assert.Equal(price, new SurvivalTuning().GetPrice(kind));
            Assert.Equal(0, ProvisionPricing.Resolve(price, "Sandbox"));
        }

        private static SurvivalStateMessage RoundTrip(SurvivalStateMessage message)
        {
            using (var stream = new MemoryStream())
            {
                new SurvivalStatePacket { Message = message }.Serialize(new BinaryWriter(stream));
                stream.Position = 0; var packet = new SurvivalStatePacket(); packet.Deserialize(new BinaryReader(stream));
                return packet.Message;
            }
        }
    }
}
