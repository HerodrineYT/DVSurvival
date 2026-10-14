namespace DVSurvival.Core
{
    public enum ProvisionKind : byte
    {
        None = 0,
        Meal = 1,
        Water = 2,
        Coffee = 3,
        FirstAid = 4,
        HeatPack = 5,
        // 6 was the removed alcoholic item; never reuse it in saved records or network commands.
        CannedFood = 7,
        Tea = 8
    }

    public enum SurvivalActionKind : byte
    {
        None = 0,
        Consume = 1,
        Purchase = 2,
        Sleep = 3,
        Trauma = 4,
        ConsumePhysical = 5,
        SetCabHeater = 6,
        SleepWithoutTimeAdvance = 7,
        ConsumePortion = 8
    }

    public enum TraumaKind : byte
    {
        None = 0,
        Fall = 1,
        TrainCollision = 2,
        TrainDismount = 3
    }

    public enum SurvivalResultCode : byte
    {
        None = 0,
        Success = 1,
        NotReady = 2,
        InvalidRequest = 3,
        ProtocolMismatch = 4,
        NoStock = 5,
        NotNeeded = 6,
        NotNearShop = 7,
        NotNearBed = 8,
        NotEnoughMoney = 9,
        RateLimited = 10,
        UnsupportedMultiplayerVersion = 11
    }
}
