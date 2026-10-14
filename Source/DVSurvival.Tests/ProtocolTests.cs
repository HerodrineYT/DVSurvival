using System.IO;
using DVSurvival.Core;
using DVSurvival.Multiplayer;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class ProtocolTests
    {
        [Fact]
        public void PartialItemReceiptPreservesAuthoritativeRemainder()
        {
            var copy = RoundTrip(new SurvivalStatePacket
            {
                Message = new SurvivalStateMessage
                { ItemIdentity = "d7d663e9-5a3b-420a-b75f-4a64b7c9f3d3", ItemUsedUnits = 375 }
            }, new SurvivalStatePacket());
            Assert.Equal(375, copy.Message.ItemUsedUnits);
            Assert.Equal("d7d663e9-5a3b-420a-b75f-4a64b7c9f3d3", copy.Message.ItemIdentity);
        }
        [Fact]
        public void TrainDismountActionRoundTrips()
        {
            var copy = RoundTrip(new SurvivalActionPacket
            {
                Request = new SurvivalActionRequest
                {
                    Action = SurvivalActionKind.Trauma, Trauma = TraumaKind.TrainDismount,
                    Amount = 20f, SecondaryAmount = 1.8f, SessionId = "current-host-epoch"
                }
            }, new SurvivalActionPacket());
            Assert.Equal(26, copy.Request.Protocol);
            Assert.Equal(TraumaKind.TrainDismount, copy.Request.Trauma);
            Assert.Equal(20f, copy.Request.Amount);
        }
        [Fact]
        public void PersonalSleepWithoutCalendarJumpRoundTrips()
        {
            var copy = RoundTrip(new SurvivalActionPacket
            {
                Request = new SurvivalActionRequest
                {
                    Action = SurvivalActionKind.SleepWithoutTimeAdvance,
                    Amount = 10f, SessionId = "current-host-epoch", RequestId = 19u
                }
            }, new SurvivalActionPacket());
            Assert.Equal(26, copy.Request.Protocol);
            Assert.Equal(SurvivalActionKind.SleepWithoutTimeAdvance, copy.Request.Action);
            Assert.Equal(10f, copy.Request.Amount);
            Assert.Equal(0L, copy.Request.CalendarBeforeTicks);
            Assert.Equal(0L, copy.Request.CalendarAfterTicks);
            Assert.Equal("current-host-epoch", copy.Request.SessionId);
            Assert.Equal(19u, copy.Request.RequestId);
        }

        [Theory]
        [InlineData(17)]
        [InlineData(24)]
        [InlineData(25)]
        public void OldStateProtocolIsRejectedBeforeParsingAnIncompatiblePayload(int protocol)
        {
            using (var stream = new MemoryStream())
            {
                new BinaryWriter(stream).Write(protocol);
                stream.Position = 0;
                var packet = new SurvivalStatePacket();
                packet.Deserialize(new BinaryReader(stream));
                Assert.Equal(SurvivalResultCode.ProtocolMismatch, packet.Message.Result);
                Assert.Equal(protocol, packet.Message.Protocol);
            }
        }

        [Theory]
        [InlineData(17)]
        [InlineData(24)]
        [InlineData(25)]
        public void OldActionProtocolIsRejectedBeforeReadingNewCalendarFields(int protocol)
        {
            using (var stream = new MemoryStream())
            {
                new BinaryWriter(stream).Write(protocol);
                stream.Position = 0;
                var packet = new SurvivalActionPacket();
                packet.Deserialize(new BinaryReader(stream));
                Assert.Equal(protocol, packet.Request.Protocol);
                Assert.Equal(0u, packet.Request.RequestId);
            }
        }

        [Fact]
        public void EnvironmentPacketPreservesTemperatureAbove60()
        {
            var packet = new SurvivalEnvironmentPacket();
            packet.Report.Environment.AmbientTemperatureCelsius = 120f;
            packet.Report.Environment.IsWinter = true;
            var copy = RoundTrip(packet, new SurvivalEnvironmentPacket());
            Assert.Equal(120f, copy.Report.Environment.AmbientTemperatureCelsius);
            Assert.True(copy.Report.Environment.IsWinter);
            Assert.True(copy.Report.Environment.IsValid());
        }
        [Theory]
        [InlineData("0.1.15.4", "0.1.15.8", -1)]
        [InlineData("0.1.15.8-Beta", "0.1.15.8", 0)]
        [InlineData("0.1.16", "0.1.15.8", 1)]
        public void VersionComparisonHandlesFourPartAndTaggedVersions(string left, string right, int sign)
        {
            var actual = VersionUtil.Compare(left, right);
            Assert.Equal(sign, actual == 0 ? 0 : (actual < 0 ? -1 : 1));
        }

        [Fact]
        public void StatePacketRoundTripsStateResultSessionAndHostPrices()
        {
            var packet = new SurvivalStatePacket
            {
                Message = new SurvivalStateMessage
                {
                    Sequence = 17,
                    RequestId = 9,
                    Result = SurvivalResultCode.Success,
                    SessionId = "95f8e3f0-2e4a-42c5-b8cb-569cb31cb8b2",
                    StatusKey = "purchase_ok",
                    MealPrice = 90,
                    WaterPrice = 40,
                    CoffeePrice = 75,
                    FirstAidPrice = 350,
                    HeatPackPrice = 120,
                    CabHeaterCarId = "DE2-42-cab-heater",
                    CabHeaterLevel = 2f / 3f,
                    State = new SurvivalState { Hunger = 42f, Rest = 10f, Meals = 7, Revision = 99, LowRestGameHours = 126.75d, ExhaustionHoursRemaining = 2.25f, CoffeeUsesSinceSleep = 6 }
                }
            };

            var copy = RoundTrip(packet, new SurvivalStatePacket());

            Assert.Equal(17u, copy.Message.Sequence);
            Assert.Equal(9u, copy.Message.RequestId);
            Assert.Equal(SurvivalResultCode.Success, copy.Message.Result);
            Assert.Equal("purchase_ok", copy.Message.StatusKey);
            Assert.Equal(350, copy.Message.GetPrice(ProvisionKind.FirstAid));
            Assert.Equal("DE2-42-cab-heater", copy.Message.CabHeaterCarId);
            Assert.Equal(2f / 3f, copy.Message.CabHeaterLevel);
            Assert.Equal(42f, copy.Message.State.Hunger);
            Assert.Equal(7, copy.Message.State.Meals);
            Assert.Equal(99u, copy.Message.State.Revision);
            Assert.Equal(126.75d, copy.Message.State.LowRestGameHours);
            Assert.Equal(2.25f, copy.Message.State.ExhaustionHoursRemaining);
            Assert.Equal(26, copy.Message.Protocol);
            Assert.Equal(6, copy.Message.State.CoffeeUsesSinceSleep);
        }

        [Fact]
        public void CabHeaterActionRoundTripsLongTrainCarIdentity()
        {
            var carId = new string('c', 80);
            var copy = RoundTrip(new SurvivalActionPacket
            {
                Request = new SurvivalActionRequest
                {
                    Action = SurvivalActionKind.SetCabHeater,
                    ItemIdentity = carId,
                    Amount = 1f
                }
            }, new SurvivalActionPacket());

            Assert.Equal(SurvivalActionKind.SetCabHeater, copy.Request.Action);
            Assert.Equal(carId, copy.Request.ItemIdentity);
            Assert.Equal(1f, copy.Request.Amount);
        }

        [Fact]
        public void CabHeaterPacketFieldsRejectOversizedTrainCarIdentities()
        {
            var oversized = new string('x', 81);
            var action = RoundTrip(new SurvivalActionPacket
            {
                Request = new SurvivalActionRequest
                {
                    Action = SurvivalActionKind.SetCabHeater,
                    ItemIdentity = oversized
                }
            }, new SurvivalActionPacket());
            var state = RoundTrip(new SurvivalStatePacket
            {
                Message = new SurvivalStateMessage
                {
                    CabHeaterCarId = oversized,
                    CabHeaterLevel = 2f / 3f
                }
            }, new SurvivalStatePacket());

            Assert.Equal(string.Empty, action.Request.ItemIdentity);
            Assert.Equal(new string('x', 80), state.Message.CabHeaterCarId);
            Assert.Equal(2f / 3f, state.Message.CabHeaterLevel);
        }

        [Fact]
        public void HelloAndActionPacketsRoundTripIdentityAndSleepDuration()
        {
            var hello = RoundTrip(new SurvivalHelloPacket
            {
                Hello = new SurvivalHello
                {
                    IdentityId = "0275a180-121e-4dca-8bd2-32014761a841",
                    ModVersion = SurvivalConstants.ModVersion
                }
            }, new SurvivalHelloPacket());
            var action = RoundTrip(new SurvivalActionPacket
            {
                Request = new SurvivalActionRequest
                {
                    SessionId = "session-15",
                    RequestId = 123,
                    Action = SurvivalActionKind.Sleep,
                    Amount = 8.5f,
                    SecondaryAmount = 1.8f,
                    CalendarBeforeTicks = 638931744000000000L,
                    CalendarAfterTicks = 638932050000000000L,
                    Trauma = TraumaKind.Fall
                }
            }, new SurvivalActionPacket());

            Assert.Equal("0275a180-121e-4dca-8bd2-32014761a841", hello.Hello.IdentityId);
            Assert.Equal(123u, action.Request.RequestId);
            Assert.Equal("session-15", action.Request.SessionId);
            Assert.Equal(SurvivalActionKind.Sleep, action.Request.Action);
            Assert.Equal(8.5f, action.Request.Amount);
            Assert.Equal(1.8f, action.Request.SecondaryAmount);
            Assert.Equal(638931744000000000L, action.Request.CalendarBeforeTicks);
            Assert.Equal(638932050000000000L, action.Request.CalendarAfterTicks);
            Assert.Equal(TraumaKind.Fall, action.Request.Trauma);
        }

        [Fact]
        public void EnvironmentPacketRoundTripsPersonalCabClimate()
        {
            var packet = RoundTrip(new SurvivalEnvironmentPacket
            {
                Report = new SurvivalEnvironmentReport
                {
                    Sequence = 44,
                    SessionId = "current-host-epoch",
                    IsTravelling = true,
                    Environment = new SurvivalEnvironment
                    {
                        AmbientTemperatureCelsius = 37.5f,
                        Exposure = 0.05f,
                        ShelterWarmthCelsius = 0f,
                        ThermalRecoveryMultiplier = 2.15f
                    }
                }
            }, new SurvivalEnvironmentPacket());

            Assert.Equal(44u, packet.Report.Sequence);
            Assert.Equal("current-host-epoch", packet.Report.SessionId);
            Assert.True(packet.Report.IsTravelling);
            Assert.Equal(37.5f, packet.Report.Environment.AmbientTemperatureCelsius);
            Assert.Equal(2.15f, packet.Report.Environment.ThermalRecoveryMultiplier);
            Assert.True(packet.Report.Environment.IsValid());
        }

        [Theory]
        [InlineData(17)]
        [InlineData(21)]
        [InlineData(22)]
        [InlineData(23)]
        [InlineData(24)]
        [InlineData(25)]
        public void OldEnvironmentPacketIsRejectedBeforeReadingTravelFields(int protocol)
        {
            using (var stream = new MemoryStream())
            {
                new BinaryWriter(stream).Write(protocol);
                stream.Position = 0;
                var packet = new SurvivalEnvironmentPacket();
                packet.Deserialize(new BinaryReader(stream));
                Assert.Equal(protocol, packet.Report.Protocol);
                Assert.False(packet.Report.IsTravelling);
            }
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void HostConsumptionSettingAndExpandedCoffeeCountRoundTrip(bool disabled)
        {
            var copy = RoundTrip(new SurvivalStatePacket
            {
                Message = new SurvivalStateMessage
                {
                    DisableNeedsConsumption = disabled,
                    State = new SurvivalState { CoffeeUsesSinceSleep = 25 }
                }
            }, new SurvivalStatePacket());
            Assert.Equal(disabled, copy.Message.DisableNeedsConsumption);
            Assert.Equal(25, copy.Message.State.CoffeeUsesSinceSleep);
        }

        private static T RoundTrip<T>(T source, T destination)
            where T : MPAPI.Interfaces.Packets.ISerializablePacket
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    source.Serialize(writer);
                stream.Position = 0;
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                    destination.Deserialize(reader);
                return destination;
            }
        }
    }
}
