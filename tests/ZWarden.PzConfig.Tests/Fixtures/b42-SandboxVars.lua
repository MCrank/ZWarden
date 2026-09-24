-- ZWarden synthetic fixture shaped like a Build 42 (42.20.4) <name>_SandboxVars.lua: every vanilla key, in
-- file order, plus one mod-added table. Comments are ZWarden-authored; enum lines use placeholder labels.
SandboxVars = {
    VERSION = 6,
    -- Fixture help for Zombies.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    Zombies = 4,
    -- Fixture help for Distribution.
    -- 1 = Choice 1
    -- 2 = Choice 2
    Distribution = 1,
    -- Fixture help for ZombieVoronoiNoise.
    ZombieVoronoiNoise = true,
    -- Fixture help for ZombieRespawn.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    ZombieRespawn = 4,
    -- Fixture help for ZombieMigrate.
    ZombieMigrate = true,
    -- Fixture help for DayLength.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    -- 8 = Choice 8
    -- 9 = Choice 9
    -- 10 = Choice 10
    -- 11 = Choice 11
    -- 12 = Choice 12
    -- 13 = Choice 13
    -- 14 = Choice 14
    -- 15 = Choice 15
    -- 16 = Choice 16
    -- 17 = Choice 17
    -- 18 = Choice 18
    -- 19 = Choice 19
    -- 20 = Choice 20
    -- 21 = Choice 21
    -- 22 = Choice 22
    -- 23 = Choice 23
    -- 24 = Choice 24
    -- 25 = Choice 25
    -- 26 = Choice 26
    -- 27 = Choice 27
    DayLength = 4,
    StartYear = 1,
    -- Fixture help for StartMonth.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    -- 8 = Choice 8
    -- 9 = Choice 9
    -- 10 = Choice 10
    -- 11 = Choice 11
    -- 12 = Choice 12
    StartMonth = 7,
    -- Fixture help for StartDay.
    StartDay = 9,
    -- Fixture help for StartTime.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    -- 8 = Choice 8
    -- 9 = Choice 9
    StartTime = 2,
    -- Fixture help for DayNightCycle.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    DayNightCycle = 1,
    -- Fixture help for ClimateCycle.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    ClimateCycle = 1,
    -- Fixture help for FogCycle.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    FogCycle = 1,
    -- Fixture help for WaterShut.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    -- 8 = Choice 8
    -- 9 = Choice 9
    WaterShut = 2,
    -- Fixture help for ElecShut.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    -- 8 = Choice 8
    -- 9 = Choice 9
    ElecShut = 2,
    -- Fixture help for AlarmDecay.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AlarmDecay = 2,
    -- Fixture help for WaterShutModifier. Min: -1 Max: 2147483647 Default: 14
    WaterShutModifier = 14,
    -- Fixture help for ElecShutModifier. Min: -1 Max: 2147483647 Default: 14
    ElecShutModifier = 14,
    -- Fixture help for AlarmDecayModifier. Min: -1 Max: 2147483647 Default: 14
    AlarmDecayModifier = 14,
    -- Fixture help for FoodLootNew. Min: 0.00 Max: 4.00 Default: 0.80
    FoodLootNew = 0.8,
    -- Fixture help for LiteratureLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    LiteratureLootNew = 0.6,
    -- Fixture help for SkillBookLoot. Min: 0.00 Max: 4.00 Default: 0.60
    SkillBookLoot = 0.6,
    -- Fixture help for RecipeResourceLoot. Min: 0.00 Max: 4.00 Default: 0.60
    RecipeResourceLoot = 0.6,
    -- Fixture help for MedicalLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    MedicalLootNew = 0.6,
    -- Fixture help for SurvivalGearsLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    SurvivalGearsLootNew = 0.6,
    -- Fixture help for CannedFoodLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    CannedFoodLootNew = 0.6,
    -- Fixture help for WeaponLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    WeaponLootNew = 0.6,
    -- Fixture help for RangedWeaponLootNew. Min: 0.00 Max: 4.00 Default: 1.20
    RangedWeaponLootNew = 1.2,
    -- Fixture help for AmmoLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    AmmoLootNew = 0.6,
    -- Fixture help for MechanicsLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    MechanicsLootNew = 0.6,
    -- Fixture help for OtherLootNew. Min: 0.00 Max: 4.00 Default: 0.80
    OtherLootNew = 0.8,
    -- Fixture help for ClothingLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    ClothingLootNew = 0.6,
    -- Fixture help for ContainerLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    ContainerLootNew = 0.6,
    -- Fixture help for KeyLootNew. Min: 0.00 Max: 4.00 Default: 0.40
    KeyLootNew = 0.4,
    -- Fixture help for MediaLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    MediaLootNew = 0.6,
    -- Fixture help for MementoLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    MementoLootNew = 0.6,
    -- Fixture help for CookwareLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    CookwareLootNew = 0.6,
    -- Fixture help for MaterialLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    MaterialLootNew = 0.6,
    -- Fixture help for FarmingLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    FarmingLootNew = 0.6,
    -- Fixture help for ToolLootNew. Min: 0.00 Max: 4.00 Default: 0.60
    ToolLootNew = 0.6,
    -- Fixture help for RollsMultiplier. Min: 0.10 Max: 100.00 Default: 1.00
    RollsMultiplier = 1.0,
    -- Fixture help for LootItemRemovalList.
    LootItemRemovalList = "",
    -- Fixture help for RemoveStoryLoot.
    RemoveStoryLoot = false,
    -- Fixture help for RemoveZombieLoot.
    RemoveZombieLoot = false,
    -- Fixture help for ZombiePopLootEffect. Min: 0 Max: 20 Default: 0
    ZombiePopLootEffect = 0,
    -- Fixture help for InsaneLootFactor. Min: 0.00 Max: 0.20 Default: 0.05
    InsaneLootFactor = 0.05,
    -- Fixture help for ExtremeLootFactor. Min: 0.05 Max: 0.60 Default: 0.20
    ExtremeLootFactor = 0.2,
    -- Fixture help for RareLootFactor. Min: 0.20 Max: 1.00 Default: 0.60
    RareLootFactor = 0.6,
    -- Fixture help for NormalLootFactor. Min: 0.60 Max: 2.00 Default: 1.00
    NormalLootFactor = 1.0,
    -- Fixture help for CommonLootFactor. Min: 1.00 Max: 3.00 Default: 2.00
    CommonLootFactor = 2.0,
    -- Fixture help for AbundantLootFactor. Min: 2.00 Max: 4.00 Default: 3.00
    AbundantLootFactor = 3.0,
    -- Fixture help for Temperature.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    Temperature = 3,
    -- Fixture help for Rain.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    Rain = 3,
    -- Fixture help for ErosionSpeed.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    ErosionSpeed = 4,
    -- Fixture help for ErosionDays. Min: -1 Max: 36500 Default: 0
    ErosionDays = 0,
    -- Fixture help for Farming.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    Farming = 3,
    -- Fixture help for CompostTime.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    -- 8 = Choice 8
    CompostTime = 2,
    -- Fixture help for StatsDecrease.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    StatsDecrease = 3,
    -- Fixture help for NatureAbundance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    NatureAbundance = 3,
    -- Fixture help for Alarm.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    Alarm = 4,
    -- Fixture help for LockedHouses.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    LockedHouses = 6,
    -- Fixture help for StarterKit.
    StarterKit = false,
    -- Fixture help for Nutrition.
    Nutrition = true,
    -- Fixture help for FoodRotSpeed.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    FoodRotSpeed = 3,
    -- Fixture help for FridgeFactor.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    FridgeFactor = 3,
    -- Fixture help for SeenHoursPreventLootRespawn. Min: 0 Max: 2147483647 Default: 0
    SeenHoursPreventLootRespawn = 0,
    -- Fixture help for HoursForLootRespawn. Min: 0 Max: 2147483647 Default: 0
    HoursForLootRespawn = 0,
    -- Fixture help for MaxItemsForLootRespawn. Min: 0 Max: 2147483647 Default: 5
    MaxItemsForLootRespawn = 5,
    -- Fixture help for ConstructionPreventsLootRespawn.
    ConstructionPreventsLootRespawn = true,
    -- Fixture help for WorldItemRemovalList.
    WorldItemRemovalList = "",
    -- Fixture help for HoursForWorldItemRemoval. Min: 0.00 Max: 2147483647.00 Default: 24.00
    HoursForWorldItemRemoval = 24.0,
    -- Fixture help for ItemRemovalListBlacklistToggle.
    ItemRemovalListBlacklistToggle = false,
    -- Fixture help for TimeSinceApo.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    -- 8 = Choice 8
    -- 9 = Choice 9
    -- 10 = Choice 10
    -- 11 = Choice 11
    -- 12 = Choice 12
    -- 13 = Choice 13
    TimeSinceApo = 1,
    -- Fixture help for PlantResilience.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    PlantResilience = 3,
    -- Fixture help for PlantAbundance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    PlantAbundance = 3,
    -- Fixture help for EndRegen.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    EndRegen = 3,
    -- Fixture help for Helicopter.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    Helicopter = 2,
    -- Fixture help for MetaEvent.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    MetaEvent = 2,
    -- Fixture help for SleepingEvent.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    SleepingEvent = 1,
    -- Fixture help for GeneratorFuelConsumption. Min: 0.00 Max: 100.00 Default: 0.10
    GeneratorFuelConsumption = 0.1,
    -- Fixture help for GeneratorSpawning.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    GeneratorSpawning = 4,
    -- Fixture help for AnnotatedMapChance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnnotatedMapChance = 4,
    -- Fixture help for CharacterFreePoints. Min: -100 Max: 100 Default: 0
    CharacterFreePoints = 0,
    -- Fixture help for ConstructionBonusPoints.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    ConstructionBonusPoints = 3,
    -- Fixture help for NightDarkness.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    NightDarkness = 3,
    -- Fixture help for NightLength.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    NightLength = 3,
    -- Fixture help for BoneFracture.
    BoneFracture = true,
    -- Fixture help for InjurySeverity.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    InjurySeverity = 2,
    -- Fixture help for HoursForCorpseRemoval. Min: -1.00 Max: 2147483647.00 Default: 216.00
    HoursForCorpseRemoval = 216.0,
    -- Fixture help for DecayingCorpseHealthImpact.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    DecayingCorpseHealthImpact = 3,
    -- Fixture help for ZombieHealthImpact.
    ZombieHealthImpact = false,
    -- Fixture help for BloodLevel.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    BloodLevel = 3,
    -- Fixture help for ClothingDegradation.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    ClothingDegradation = 3,
    -- Fixture help for FireSpread.
    FireSpread = true,
    -- Fixture help for DaysForRottenFoodRemoval. Min: -1 Max: 2147483647 Default: -1
    DaysForRottenFoodRemoval = -1,
    -- Fixture help for AllowExteriorGenerator.
    AllowExteriorGenerator = true,
    -- Fixture help for MaxFogIntensity.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    MaxFogIntensity = 1,
    -- Fixture help for MaxRainFxIntensity.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    MaxRainFxIntensity = 1,
    -- Fixture help for EnableSnowOnGround.
    EnableSnowOnGround = true,
    -- Fixture help for AttackBlockMovements.
    AttackBlockMovements = true,
    -- Fixture help for SurvivorHouseChance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    SurvivorHouseChance = 3,
    -- Fixture help for VehicleStoryChance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    VehicleStoryChance = 3,
    -- Fixture help for ZoneStoryChance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    ZoneStoryChance = 3,
    -- Fixture help for AllClothesUnlocked.
    AllClothesUnlocked = false,
    -- Fixture help for EnableTaintedWaterText.
    EnableTaintedWaterText = true,
    -- Fixture help for EnableVehicles.
    EnableVehicles = true,
    -- Fixture help for CarSpawnRate.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    CarSpawnRate = 3,
    -- Fixture help for ZombieAttractionMultiplier. Min: 0.00 Max: 100.00 Default: 1.00
    ZombieAttractionMultiplier = 1.0,
    -- Fixture help for VehicleEasyUse.
    VehicleEasyUse = false,
    -- Fixture help for InitialGas.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    InitialGas = 2,
    -- Fixture help for FuelStationGasInfinite.
    FuelStationGasInfinite = false,
    -- Fixture help for FuelStationGasMin. Min: 0.00 Max: 1.00 Default: 0.00
    FuelStationGasMin = 0.0,
    -- Fixture help for FuelStationGasMax. Min: 0.00 Max: 1.00 Default: 0.80
    FuelStationGasMax = 0.8,
    -- Fixture help for FuelStationGasEmptyChance. Min: 0 Max: 100 Default: 20
    FuelStationGasEmptyChance = 20,
    -- Fixture help for LockedCar.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    LockedCar = 4,
    -- Fixture help for CarGasConsumption. Min: 0.00 Max: 100.00 Default: 1.00
    CarGasConsumption = 1.0,
    -- Fixture help for CarGeneralCondition.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    CarGeneralCondition = 3,
    -- Fixture help for CarDamageOnImpact.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    CarDamageOnImpact = 3,
    -- Fixture help for DamageToPlayerFromHitByACar.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    DamageToPlayerFromHitByACar = 1,
    -- Fixture help for TrafficJam.
    TrafficJam = true,
    -- Fixture help for CarAlarm.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    CarAlarm = 3,
    -- Fixture help for PlayerDamageFromCrash.
    PlayerDamageFromCrash = true,
    -- Fixture help for SirenShutoffHours. Min: 0.00 Max: 168.00 Default: 0.00
    SirenShutoffHours = 0.0,
    -- Fixture help for ChanceHasGas.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    ChanceHasGas = 2,
    -- Fixture help for RecentlySurvivorVehicles.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    RecentlySurvivorVehicles = 2,
    -- Fixture help for MultiHitZombies.
    MultiHitZombies = false,
    -- Fixture help for RearVulnerability.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    RearVulnerability = 3,
    -- Fixture help for SirenEffectsZombies.
    SirenEffectsZombies = true,
    -- Fixture help for AnimalStatsModifier.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalStatsModifier = 4,
    -- Fixture help for AnimalMetaStatsModifier.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalMetaStatsModifier = 4,
    -- Fixture help for AnimalPregnancyTime.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalPregnancyTime = 4,
    -- Fixture help for AnimalAgeModifier.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalAgeModifier = 4,
    -- Fixture help for AnimalMilkIncModifier.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalMilkIncModifier = 4,
    -- Fixture help for AnimalWoolIncModifier.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalWoolIncModifier = 4,
    -- Fixture help for AnimalRanchChance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    -- 7 = Choice 7
    AnimalRanchChance = 5,
    -- Fixture help for AnimalGrassRegrowTime. Min: 1 Max: 9999 Default: 240
    AnimalGrassRegrowTime = 240,
    -- Fixture help for AnimalMetaPredator.
    AnimalMetaPredator = false,
    -- Fixture help for AnimalMatingSeason.
    AnimalMatingSeason = true,
    -- Fixture help for AnimalEggHatch.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalEggHatch = 4,
    -- Fixture help for AnimalSoundAttractZombies.
    AnimalSoundAttractZombies = true,
    -- Fixture help for AnimalTrackChance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalTrackChance = 4,
    -- Fixture help for AnimalPathChance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    -- 6 = Choice 6
    AnimalPathChance = 4,
    -- Fixture help for MaximumRatIndex. Min: 0 Max: 50 Default: 25
    MaximumRatIndex = 25,
    -- Fixture help for DaysUntilMaximumRatIndex. Min: 0 Max: 365 Default: 90
    DaysUntilMaximumRatIndex = 90,
    -- Fixture help for MetaKnowledge.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    MetaKnowledge = 3,
    -- Fixture help for SeeNotLearntRecipe.
    SeeNotLearntRecipe = true,
    -- Fixture help for MaximumLootedBuildingRooms. Min: 0 Max: 200 Default: 50
    MaximumLootedBuildingRooms = 50,
    -- Fixture help for EnablePoisoning.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    EnablePoisoning = 1,
    -- Fixture help for MaggotSpawn.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    MaggotSpawn = 1,
    -- Fixture help for LightBulbLifespan. Min: 0.00 Max: 1000.00 Default: 2.00
    LightBulbLifespan = 2.0,
    -- Fixture help for FishAbundance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    -- 5 = Choice 5
    FishAbundance = 2,
    -- Fixture help for LevelForMediaXPCutoff. Min: 0 Max: 10 Default: 3
    LevelForMediaXPCutoff = 3,
    -- Fixture help for LevelForDismantleXPCutoff. Min: 0 Max: 10 Default: 0
    LevelForDismantleXPCutoff = 0,
    -- Fixture help for BloodSplatLifespanDays. Min: 0 Max: 365 Default: 0
    BloodSplatLifespanDays = 0,
    -- Fixture help for LiteratureCooldown. Min: 1 Max: 365 Default: 45
    LiteratureCooldown = 45,
    -- Fixture help for NegativeTraitsPenalty.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    -- 4 = Choice 4
    NegativeTraitsPenalty = 1,
    -- Fixture help for MinutesPerPage. Min: 0.00 Max: 60.00 Default: 2.00
    MinutesPerPage = 2.0,
    -- Fixture help for KillInsideCrops.
    KillInsideCrops = true,
    -- Fixture help for PlantGrowingSeasons.
    PlantGrowingSeasons = true,
    -- Fixture help for PlaceDirtAboveground.
    PlaceDirtAboveground = false,
    -- Fixture help for FarmingSpeedNew. Min: 0.10 Max: 100.00 Default: 1.00
    FarmingSpeedNew = 1.0,
    -- Fixture help for FarmingAmountNew. Min: 0.10 Max: 10.00 Default: 1.00
    FarmingAmountNew = 1.0,
    -- Fixture help for MaximumLooted. Min: 0 Max: 200 Default: 25
    MaximumLooted = 25,
    -- Fixture help for DaysUntilMaximumLooted. Min: 0 Max: 3650 Default: 90
    DaysUntilMaximumLooted = 90,
    -- Fixture help for RuralLooted. Min: 0.00 Max: 2.00 Default: 0.50
    RuralLooted = 0.5,
    -- Fixture help for MaximumDiminishedLoot. Min: 0 Max: 100 Default: 20
    MaximumDiminishedLoot = 20,
    -- Fixture help for DaysUntilMaximumDiminishedLoot. Min: 0 Max: 3650 Default: 3650
    DaysUntilMaximumDiminishedLoot = 3650,
    -- Fixture help for MuscleStrainFactor. Min: 0.00 Max: 10.00 Default: 0.70
    MuscleStrainFactor = 0.7,
    -- Fixture help for DiscomfortFactor. Min: 0.00 Max: 10.00 Default: 0.80
    DiscomfortFactor = 0.8,
    -- Fixture help for WoundInfectionFactor. Min: 0.00 Max: 10.00 Default: 1.00
    WoundInfectionFactor = 1.0,
    -- Fixture help for NoBlackClothes.
    NoBlackClothes = true,
    -- Fixture help for EasyClimbing.
    EasyClimbing = false,
    -- Fixture help for MaximumFireFuelHours. Min: 1 Max: 168 Default: 8
    MaximumFireFuelHours = 8,
    -- Fixture help for FirearmUseDamageChance.
    -- 1 = Choice 1
    -- 2 = Choice 2
    -- 3 = Choice 3
    FirearmUseDamageChance = 2,
    -- Fixture help for FirearmNoiseMultiplier. Min: 0.20 Max: 2.00 Default: 1.00
    FirearmNoiseMultiplier = 1.0,
    -- Fixture help for FirearmJamMultiplier. Min: 0.00 Max: 10.00 Default: 1.00
    FirearmJamMultiplier = 1.0,
    -- Fixture help for FirearmMoodleMultiplier. Min: 0.00 Max: 10.00 Default: 1.00
    FirearmMoodleMultiplier = 1.0,
    -- Fixture help for FirearmWeatherMultiplier. Min: 0.00 Max: 10.00 Default: 1.00
    FirearmWeatherMultiplier = 1.0,
    -- Fixture help for FirearmHeadGearEffect.
    FirearmHeadGearEffect = true,
    -- Fixture help for ClayLakeChance. Min: 0.00 Max: 1.00 Default: 0.05
    ClayLakeChance = 0.05,
    -- Fixture help for ClayRiverChance. Min: 0.00 Max: 1.00 Default: 0.05
    ClayRiverChance = 0.05,
    -- Fixture help for GeneratorTileRange. Min: 1 Max: 100 Default: 20
    GeneratorTileRange = 20,
    -- Fixture help for GeneratorVerticalPowerRange. Min: 1 Max: 15 Default: 3
    GeneratorVerticalPowerRange = 3,
    Basement = {
        -- Fixture help for SpawnFrequency.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        -- 5 = Choice 5
        -- 6 = Choice 6
        -- 7 = Choice 7
        SpawnFrequency = 4,
    },
    Map = {
        -- Fixture help for AllowMiniMap.
        AllowMiniMap = false,
        -- Fixture help for AllowWorldMap.
        AllowWorldMap = true,
        -- Fixture help for MapAllKnown.
        MapAllKnown = false,
        -- Fixture help for MapNeedsLight.
        MapNeedsLight = true,
    },
    ZombieLore = {
        -- Fixture help for Speed.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        Speed = 4,
        -- Fixture help for SprinterPercentage. Min: 0 Max: 100 Default: 0
        SprinterPercentage = 0,
        -- Fixture help for Strength.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        Strength = 2,
        -- Fixture help for Toughness.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        Toughness = 4,
        -- Fixture help for Transmission.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        Transmission = 1,
        -- Fixture help for Mortality.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        -- 5 = Choice 5
        -- 6 = Choice 6
        -- 7 = Choice 7
        Mortality = 5,
        -- Fixture help for Reanimate.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        -- 5 = Choice 5
        -- 6 = Choice 6
        Reanimate = 3,
        -- Fixture help for Cognition.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        Cognition = 3,
        -- Fixture help for DoorOpeningPercentage. Min: 0 Max: 100 Default: 0
        DoorOpeningPercentage = 0,
        -- Fixture help for CrawlUnderVehicle.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        -- 5 = Choice 5
        -- 6 = Choice 6
        -- 7 = Choice 7
        CrawlUnderVehicle = 5,
        -- Fixture help for Memory.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        -- 5 = Choice 5
        -- 6 = Choice 6
        Memory = 2,
        -- Fixture help for Sight.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        -- 5 = Choice 5
        Sight = 5,
        -- Fixture help for Hearing.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        -- 5 = Choice 5
        Hearing = 5,
        -- Fixture help for SpottedLogic.
        SpottedLogic = true,
        -- Fixture help for ThumpNoChasing.
        ThumpNoChasing = false,
        -- Fixture help for ThumpOnConstruction.
        ThumpOnConstruction = true,
        -- Fixture help for ActiveOnly.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        ActiveOnly = 1,
        -- Fixture help for TriggerHouseAlarm.
        TriggerHouseAlarm = true,
        -- Fixture help for ZombiesDragDown.
        ZombiesDragDown = true,
        -- Fixture help for ZombiesCrawlersDragDown.
        ZombiesCrawlersDragDown = false,
        -- Fixture help for ZombiesFenceLunge.
        ZombiesFenceLunge = true,
        -- Fixture help for ZombiesArmorFactor. Min: 0.00 Max: 100.00 Default: 2.00
        ZombiesArmorFactor = 2.0,
        -- Fixture help for ZombiesMaxDefense. Min: 0 Max: 100 Default: 85
        ZombiesMaxDefense = 85,
        -- Fixture help for ChanceOfAttachedWeapon. Min: 0 Max: 100 Default: 6
        ChanceOfAttachedWeapon = 6,
        -- Fixture help for ZombiesFallDamage. Min: 0.00 Max: 100.00 Default: 1.00
        ZombiesFallDamage = 1.0,
        -- Fixture help for DisableFakeDead.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        DisableFakeDead = 1,
        -- Fixture help for PlayerSpawnZombieRemoval.
        -- 1 = Choice 1
        -- 2 = Choice 2
        -- 3 = Choice 3
        -- 4 = Choice 4
        PlayerSpawnZombieRemoval = 1,
        -- Fixture help for FenceThumpersRequired. Min: -1 Max: 100 Default: 25
        FenceThumpersRequired = 25,
        -- Fixture help for FenceDamageMultiplier. Min: 0.01 Max: 100.00 Default: 1.00
        FenceDamageMultiplier = 1.0,
    },
    ZombieConfig = {
        -- Fixture help for PopulationMultiplier. Min: 0.00 Max: 4.00 Default: 0.65
        PopulationMultiplier = 0.65,
        -- Fixture help for PopulationStartMultiplier. Min: 0.00 Max: 4.00 Default: 1.00
        PopulationStartMultiplier = 1.0,
        -- Fixture help for PopulationPeakMultiplier. Min: 0.00 Max: 4.00 Default: 1.50
        PopulationPeakMultiplier = 1.5,
        -- Fixture help for PopulationPeakDay. Min: 1 Max: 365 Default: 28
        PopulationPeakDay = 28,
        -- Fixture help for RespawnHours. Min: 0.00 Max: 8760.00 Default: 0.00
        RespawnHours = 0.0,
        -- Fixture help for RespawnUnseenHours. Min: 0.00 Max: 8760.00 Default: 0.00
        RespawnUnseenHours = 0.0,
        -- Fixture help for RespawnMultiplier. Min: 0.00 Max: 1.00 Default: 0.00
        RespawnMultiplier = 0.0,
        -- Fixture help for RedistributeHours. Min: 0.00 Max: 8760.00 Default: 12.00
        RedistributeHours = 12.0,
        -- Fixture help for FollowSoundDistance. Min: 10 Max: 1000 Default: 100
        FollowSoundDistance = 100,
        -- Fixture help for RallyGroupSize. Min: 0 Max: 1000 Default: 20
        RallyGroupSize = 20,
        -- Fixture help for RallyGroupSizeVariance. Min: 0 Max: 100 Default: 50
        RallyGroupSizeVariance = 50,
        -- Fixture help for RallyTravelDistance. Min: 5 Max: 50 Default: 20
        RallyTravelDistance = 20,
        -- Fixture help for RallyGroupSeparation. Min: 5 Max: 25 Default: 15
        RallyGroupSeparation = 15,
        -- Fixture help for RallyGroupRadius. Min: 1 Max: 10 Default: 3
        RallyGroupRadius = 3,
        -- Fixture help for ZombiesCountBeforeDelete. Min: 0 Max: 5000 Default: 300
        ZombiesCountBeforeDelete = 300,
    },
    MultiplierConfig = {
        -- Fixture help for Global. Min: 0.00 Max: 1000.00 Default: 1.00
        Global = 1.0,
        -- Fixture help for GlobalToggle.
        GlobalToggle = true,
        -- Fixture help for Fitness. Min: 0.00 Max: 1000.00 Default: 1.00
        Fitness = 1.0,
        -- Fixture help for Strength. Min: 0.00 Max: 1000.00 Default: 1.00
        Strength = 1.0,
        -- Fixture help for Sprinting. Min: 0.00 Max: 1000.00 Default: 1.00
        Sprinting = 1.0,
        -- Fixture help for Lightfoot. Min: 0.00 Max: 1000.00 Default: 1.00
        Lightfoot = 1.0,
        -- Fixture help for Nimble. Min: 0.00 Max: 1000.00 Default: 1.00
        Nimble = 1.0,
        -- Fixture help for Sneak. Min: 0.00 Max: 1000.00 Default: 1.00
        Sneak = 1.0,
        -- Fixture help for Axe. Min: 0.00 Max: 1000.00 Default: 1.00
        Axe = 1.0,
        -- Fixture help for Blunt. Min: 0.00 Max: 1000.00 Default: 1.00
        Blunt = 1.0,
        -- Fixture help for SmallBlunt. Min: 0.00 Max: 1000.00 Default: 1.00
        SmallBlunt = 1.0,
        -- Fixture help for LongBlade. Min: 0.00 Max: 1000.00 Default: 1.00
        LongBlade = 1.0,
        -- Fixture help for SmallBlade. Min: 0.00 Max: 1000.00 Default: 1.00
        SmallBlade = 1.0,
        -- Fixture help for Spear. Min: 0.00 Max: 1000.00 Default: 1.00
        Spear = 1.0,
        -- Fixture help for Maintenance. Min: 0.00 Max: 1000.00 Default: 1.00
        Maintenance = 1.0,
        -- Fixture help for Woodwork. Min: 0.00 Max: 1000.00 Default: 1.00
        Woodwork = 1.0,
        -- Fixture help for Cooking. Min: 0.00 Max: 1000.00 Default: 1.00
        Cooking = 1.0,
        -- Fixture help for Farming. Min: 0.00 Max: 1000.00 Default: 1.00
        Farming = 1.0,
        -- Fixture help for Doctor. Min: 0.00 Max: 1000.00 Default: 1.00
        Doctor = 1.0,
        -- Fixture help for Electricity. Min: 0.00 Max: 1000.00 Default: 1.00
        Electricity = 1.0,
        -- Fixture help for MetalWelding. Min: 0.00 Max: 1000.00 Default: 1.00
        MetalWelding = 1.0,
        -- Fixture help for Mechanics. Min: 0.00 Max: 1000.00 Default: 1.00
        Mechanics = 1.0,
        -- Fixture help for Tailoring. Min: 0.00 Max: 1000.00 Default: 1.00
        Tailoring = 1.0,
        -- Fixture help for Aiming. Min: 0.00 Max: 1000.00 Default: 1.00
        Aiming = 1.0,
        -- Fixture help for Reloading. Min: 0.00 Max: 1000.00 Default: 1.00
        Reloading = 1.0,
        -- Fixture help for Fishing. Min: 0.00 Max: 1000.00 Default: 1.00
        Fishing = 1.0,
        -- Fixture help for Trapping. Min: 0.00 Max: 1000.00 Default: 1.00
        Trapping = 1.0,
        -- Fixture help for PlantScavenging. Min: 0.00 Max: 1000.00 Default: 1.00
        PlantScavenging = 1.0,
        -- Fixture help for FlintKnapping. Min: 0.00 Max: 1000.00 Default: 1.00
        FlintKnapping = 1.0,
        -- Fixture help for Masonry. Min: 0.00 Max: 1000.00 Default: 1.00
        Masonry = 1.0,
        -- Fixture help for Pottery. Min: 0.00 Max: 1000.00 Default: 1.00
        Pottery = 1.0,
        -- Fixture help for Carving. Min: 0.00 Max: 1000.00 Default: 1.00
        Carving = 1.0,
        -- Fixture help for Husbandry. Min: 0.00 Max: 1000.00 Default: 1.00
        Husbandry = 1.0,
        -- Fixture help for Tracking. Min: 0.00 Max: 1000.00 Default: 1.00
        Tracking = 1.0,
        -- Fixture help for Blacksmith. Min: 0.00 Max: 1000.00 Default: 1.00
        Blacksmith = 1.0,
        -- Fixture help for Butchering. Min: 0.00 Max: 1000.00 Default: 1.00
        Butchering = 1.0,
        -- Fixture help for Glassmaking. Min: 0.00 Max: 1000.00 Default: 1.00
        Glassmaking = 1.0,
    },
    BetterLockpicking = {
        -- Fixture help for a mod-added option.
        Difficulty = 2,
        AllowCrowbars = true,
    },
}
