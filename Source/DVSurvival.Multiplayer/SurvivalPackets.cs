using System;
using System.IO;
using DVSurvival.Core;
using MPAPI.Interfaces.Packets;

namespace DVSurvival.Multiplayer
{
    public sealed class SurvivalHelloPacket : ISerializablePacket
    {
        public SurvivalHello Hello = new SurvivalHello();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Hello == null ? SurvivalConstants.ProtocolVersion : Hello.Protocol);
            writer.Write(Safe(Hello == null ? string.Empty : Hello.IdentityId, 80));
            writer.Write(Safe(Hello == null ? string.Empty : Hello.ModVersion, 32));
        }

        public void Deserialize(BinaryReader reader)
        {
            Hello = new SurvivalHello
            {
                Protocol = reader.ReadInt32(),
                IdentityId = Safe(reader.ReadString(), 80),
                ModVersion = Safe(reader.ReadString(), 32)
            };
        }

        private static string Safe(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }
    }

    public sealed class SurvivalActionPacket : ISerializablePacket
    {
        public SurvivalActionRequest Request = new SurvivalActionRequest();

        public void Serialize(BinaryWriter writer)
        {
            var value = Request ?? new SurvivalActionRequest();
            writer.Write(value.Protocol);
            writer.Write(value.SessionId != null && value.SessionId.Length <= 80
                ? value.SessionId : string.Empty);
            writer.Write(value.RequestId);
            writer.Write((byte)value.Action);
            writer.Write((byte)value.Provision);
            writer.Write(value.Amount);
            writer.Write(value.SecondaryAmount);
            writer.Write(value.CalendarBeforeTicks);
            writer.Write(value.CalendarAfterTicks);
            writer.Write((byte)value.Trauma);
            writer.Write(value.ItemIdentity != null && value.ItemIdentity.Length <= 80 ? value.ItemIdentity : string.Empty);
            writer.Write(value.HasNativeBed);
            writer.Write(value.BedWorldX);
            writer.Write(value.BedWorldY);
            writer.Write(value.BedWorldZ);
        }

        public void Deserialize(BinaryReader reader)
        {
            var protocol = reader.ReadInt32();
            if (protocol != SurvivalConstants.ProtocolVersion)
            {
                // The action layout changes with the protocol. Stop before reading fields that
                // do not exist in an older packet so the host can return ProtocolMismatch cleanly.
                Request = new SurvivalActionRequest { Protocol = protocol };
                return;
            }
            Request = new SurvivalActionRequest
            {
                Protocol = protocol,
                SessionId = Safe(reader.ReadString(), 80),
                RequestId = reader.ReadUInt32(),
                Action = (SurvivalActionKind)reader.ReadByte(),
                Provision = (ProvisionKind)reader.ReadByte(),
                Amount = reader.ReadSingle(),
                SecondaryAmount = reader.ReadSingle(),
                CalendarBeforeTicks = reader.ReadInt64(),
                CalendarAfterTicks = reader.ReadInt64(),
                Trauma = (TraumaKind)reader.ReadByte(),
                ItemIdentity = Safe(reader.ReadString(), 80),
                HasNativeBed = reader.ReadBoolean(),
                BedWorldX = reader.ReadSingle(),
                BedWorldY = reader.ReadSingle(),
                BedWorldZ = reader.ReadSingle()
            };
        }

        private static string Safe(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }
    }

    public sealed class SurvivalEnvironmentPacket : ISerializablePacket
    {
        public SurvivalEnvironmentReport Report = new SurvivalEnvironmentReport();

        public void Serialize(BinaryWriter writer)
        {
            var report = Report ?? new SurvivalEnvironmentReport();
            var value = report.Environment ?? new SurvivalEnvironment();
            writer.Write(report.Protocol);
            writer.Write(report.Sequence);
            writer.Write(value.AmbientTemperatureCelsius);
            writer.Write(value.RainIntensity);
            writer.Write(value.WindSpeedMetersPerSecond);
            writer.Write(value.Exposure);
            writer.Write(value.Activity);
            writer.Write(value.ShelterWarmthCelsius);
            writer.Write(value.ThermalRecoveryMultiplier);
            writer.Write(value.IsWinter);
            writer.Write(report.SessionId != null && report.SessionId.Length <= 80
                ? report.SessionId : string.Empty);
            writer.Write(report.IsTravelling);
            writer.Write(report.TravelRevision);
        }

        public void Deserialize(BinaryReader reader)
        {
            var protocol = reader.ReadInt32();
            if (protocol != SurvivalConstants.ProtocolVersion)
            {
                Report = new SurvivalEnvironmentReport { Protocol = protocol };
                return;
            }
            Report = new SurvivalEnvironmentReport
            {
                Protocol = protocol,
                Sequence = reader.ReadUInt32(),
                Environment = new SurvivalEnvironment
                {
                    AmbientTemperatureCelsius = reader.ReadSingle(),
                    RainIntensity = reader.ReadSingle(),
                    WindSpeedMetersPerSecond = reader.ReadSingle(),
                    Exposure = reader.ReadSingle(),
                    Activity = reader.ReadSingle(),
                    ShelterWarmthCelsius = reader.ReadSingle(),
                    ThermalRecoveryMultiplier = reader.ReadSingle(),
                    IsWinter = reader.ReadBoolean()
                }
            };
            var sessionId = reader.ReadString();
            Report.SessionId = sessionId.Length <= 80 ? sessionId : string.Empty;
            Report.IsTravelling = reader.ReadBoolean();
            Report.TravelRevision = reader.ReadUInt32();
        }
    }

    public sealed class SurvivalStatePacket : ISerializablePacket
    {
        public SurvivalStateMessage Message = new SurvivalStateMessage();

        public void Serialize(BinaryWriter writer)
        {
            var value = Message ?? new SurvivalStateMessage();
            writer.Write(value.Protocol);
            writer.Write(value.Sequence);
            writer.Write(value.RequestId);
            writer.Write((byte)value.Result);
            writer.Write(Safe(value.SessionId, 80));
            writer.Write(Safe(value.StatusKey, 80));
            writer.Write(value.ConfigurationRevision);
            writer.Write(value.HasConfiguration);
            if (value.HasConfiguration)
            {
                writer.Write(value.MealPrice); writer.Write(value.WaterPrice); writer.Write(value.CoffeePrice);
                writer.Write(value.FirstAidPrice); writer.Write(value.HeatPackPrice);
                writer.Write(value.CannedFoodPrice); writer.Write(value.TeaPrice);
                writer.Write(value.DisableNeedsConsumption);
            }
            writer.Write(Safe(value.CabHeaterCarId, 80));
            writer.Write(value.CabHeaterLevel);
            writer.Write(Safe(value.ItemIdentity, 80));
            writer.Write(value.ItemUsedUnits);
            SurvivalStateCodec.Write(writer, value.State ?? new SurvivalState());
        }

        public void Deserialize(BinaryReader reader)
        {
            var protocol = reader.ReadInt32();
            if (protocol != SurvivalConstants.ProtocolVersion)
            {
                Message = new SurvivalStateMessage { Protocol = protocol, Result = SurvivalResultCode.ProtocolMismatch };
                return;
            }
            Message = new SurvivalStateMessage
            {
                Protocol = protocol, Sequence = reader.ReadUInt32(), RequestId = reader.ReadUInt32(),
                Result = (SurvivalResultCode)reader.ReadByte(), SessionId = Safe(reader.ReadString(), 80),
                StatusKey = Safe(reader.ReadString(), 80), ConfigurationRevision = reader.ReadUInt32(),
                HasConfiguration = reader.ReadBoolean()
            };
            if (Message.HasConfiguration)
            {
                Message.MealPrice = reader.ReadInt32(); Message.WaterPrice = reader.ReadInt32();
                Message.CoffeePrice = reader.ReadInt32(); Message.FirstAidPrice = reader.ReadInt32();
                Message.HeatPackPrice = reader.ReadInt32();
                Message.CannedFoodPrice = reader.ReadInt32(); Message.TeaPrice = reader.ReadInt32();
                Message.DisableNeedsConsumption = reader.ReadBoolean();
            }
            Message.CabHeaterCarId = Safe(reader.ReadString(), 80);
            Message.CabHeaterLevel = reader.ReadSingle();
            Message.ItemIdentity = Safe(reader.ReadString(), 80);
            Message.ItemUsedUnits = reader.ReadInt32();
            Message.State = SurvivalStateCodec.Read(reader);
        }

        private static string Safe(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }
    }
}
