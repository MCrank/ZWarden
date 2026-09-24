using System.Text.RegularExpressions;

namespace ZWarden.PzConfig;

/// <summary>
/// Where a config editor files a setting and what it calls it (#227). Display-only: it never affects validation,
/// which stays with the hand-curated schema. Vanilla keys follow the grouping of Project Zomboid's own in-game
/// server-settings screen (Build 42), extended by hand for the keys that screen leaves out, so an operator finds a
/// setting where the game puts it. A key the catalog has not seen — a new patch's or a mod's — falls back to a
/// name-prefix rule, and in the sandbox file a mod's nested option table becomes its own section. Only a truly
/// unmatched key lands in <see cref="OtherSection"/>.
/// </summary>
public static class PzSettingCatalog
{
    /// <summary>The section for a key nothing else claims; a config editor pins it last.</summary>
    public const string OtherSection = "Other";

    // Sections not in the vanilla list (a mod's table) sort after every vanilla section and before Other.
    private const int DerivedRank = 1_000;

    private static readonly Catalog Ini = new(
    [
        Section("Details",
            "DefaultPort", "PublicName", "PublicDescription", "Public", "Password", "PauseEmpty", "ResetID",
            "server_browser_announced_ip"),
        Section("Steam",
            "UDPPort", "MaxAccountsPerUser", "SteamScoreboard", "SteamVAC"),
        Section("Mods & map",
            "Mods", "WorkshopItems", "Map"),
        Section("Players",
            "MaxPlayers", "Open", "DropOffWhiteListAfterDeath", "DisplayUserName", "ShowFirstAndLastName",
            "SpawnItems", "PingLimit", "ServerPlayerID", "SleepAllowed", "SleepNeeded", "PlayerRespawnWithSelf",
            "PlayerRespawnWithOther", "RemovePlayerCorpsesOnCorpseRemoval", "TrashDeleteAll",
            "PVPMeleeWhileHitReaction", "MouseOverToSeeDisplayName", "UsernameDisguises", "HideDisguisedUserName",
            "HidePlayersBehindYou", "PlayerBumpPlayer", "MapRemotePlayerVisibility", "AllowCoop", "SpawnPoint",
            "KnockedDownAllowed", "SneakModeHideFromOtherPlayers", "DisableScoreboard", "HideAdminsInPlayerList",
            "ShowCoordinates", "LoginQueueEnabled", "LoginQueueConnectTimeout"),
        Section("PVP",
            "PVP", "SafetySystem", "ShowSafety", "SafetyToggleTimer", "SafetyCooldownTimer", "PVPMeleeDamageModifier",
            "PVPFirearmDamageModifier", "PVPLogToolChat", "PVPLogToolFile", "SafetyDisconnectDelay",
            "UsePhysicsHitReaction"),
        Section("Safehouse",
            "AdminSafehouse", "PlayerSafehouse", "SafehouseAllowTrepass", "SafehouseAllowFire", "SafehouseAllowLoot",
            "SafehouseAllowRespawn", "SafehouseDaySurvivedToClaim", "SafeHouseRemovalTime",
            "DisableSafehouseWhenOwnerConnected", "SafehouseAllowNonResidential", "SafehouseDisableDisguises",
            "MaxSafezoneSize"),
        Section("Faction",
            "Faction", "FactionDaySurvivedToCreate", "FactionPlayersRequiredForTag"),
        Section("War",
            "War", "WarStartDelay", "WarDuration", "WarSafehouseHitPoints"),
        Section("Chat",
            "GlobalChat", "AnnounceDeath", "AnnounceAnimalDeath", "ServerWelcomeMessage", "ChatMessageCharacterLimit",
            "ChatMessageSlowModeTime", "ChatStreams", "BadWordListFile", "GoodWordListFile", "BadWordPolicy",
            "BadWordReplacement"),
        Section("Loot",
            "SafehousePreventsLootRespawn", "ItemNumbersLimitPerContainer"),
        Section("Fire",
            "NoFire"),
        Section("Vehicles",
            "SpeedLimit", "CarEngineAttractionModifier", "DisableVehicleTowing", "DisableTrailerTowing",
            "DisableBurntTowing"),
        Section("Voice",
            "VoiceEnable", "VoiceMinDistance", "VoiceMaxDistance", "Voice3D"),
        Section("Admin",
            "ClientCommandFilter", "ClientActionLogs", "PerkLogs", "DisableRadioStaff", "DisableRadioAdmin",
            "DisableRadioGM", "DisableRadioOverseer", "DisableRadioModerator", "DisableRadioInvisible",
            "BanKickGlobalSound"),
        Section("Anti-cheat",
            "AntiCheatSafety", "AntiCheatSpeed", "AntiCheatNoClip", "AntiCheatHit", "AntiCheatPacketException",
            "AntiCheatPermission", "AntiCheatXP", "AntiCheatSafeHouse", "AntiCheatPlayer", "AntiCheatChecksum"),
        Section("RCON",
            "RCONPort", "RCONPassword"),
        Section("Discord",
            "DiscordEnable", "DiscordToken", "DiscordChatChannel", "DiscordLogChannel", "DiscordCommandChannel",
            "WebhookAddress"),
        Section("Backups",
            "BackupsCount", "BackupsOnStart", "BackupsOnVersionChange", "BackupsPeriod"),
        Section("UPnP",
            "UPnP"),
        Section("Performance",
            "DenyLoginOnOverloadedServer", "SwitchZombiesOwnershipEachUpdate", "MaxPacketsPerSecond",
            "MultiplayerStatisticsPeriod"),
        Section("General",
            "DoLuaChecksum", "AllowDestructionBySledgehammer", "SledgehammerOnlyInSafehouse", "SaveWorldEveryMinutes",
            "FastForwardMultiplier", "AllowNonAsciiUsername", "Seed", "BloodSplatLifespanDays",
            "UltraSpeedDoesnotAffectToAnimals"),
    ],
    [
        ("AntiCheat", "Anti-cheat"), ("Safehouse", "Safehouse"), ("SafeHouse", "Safehouse"), ("Faction", "Faction"),
        ("War", "War"), ("Discord", "Discord"), ("Voice", "Voice"), ("RCON", "RCON"), ("Backups", "Backups"),
        ("PVP", "PVP"), ("Safety", "PVP"), ("Steam", "Steam"), ("Chat", "Chat"), ("BadWord", "Chat"),
        ("GoodWord", "Chat"), ("DisableRadio", "Admin"), ("Workshop", "Mods & map"), ("Player", "Players"),
    ]);

    private static readonly Catalog SandboxVars = new(
    [
        Section("Zombies",
            "Zombies", "Distribution", "ZombieVoronoiNoise", "ZombieRespawn", "ZombieMigrate"),
        Section("Zombie lore",
            "ZombieLore.Speed", "ZombieLore.SprinterPercentage", "ZombieLore.Strength", "ZombieLore.Toughness",
            "ZombieLore.Transmission", "ZombieLore.Mortality", "ZombieLore.Reanimate", "ZombieLore.Cognition",
            "ZombieLore.DoorOpeningPercentage", "ZombieLore.CrawlUnderVehicle", "ZombieLore.Memory",
            "ZombieLore.Sight", "ZombieLore.Hearing", "ZombieLore.SpottedLogic", "ZombieLore.ThumpNoChasing",
            "ZombieLore.ThumpOnConstruction", "ZombieLore.ActiveOnly", "ZombieLore.TriggerHouseAlarm",
            "ZombieLore.ZombiesDragDown", "ZombieLore.ZombiesCrawlersDragDown", "ZombieLore.ZombiesFenceLunge",
            "ZombieLore.DisableFakeDead", "ZombieLore.ZombiesArmorFactor", "ZombieLore.ZombiesMaxDefense",
            "ZombieLore.ChanceOfAttachedWeapon", "ZombieLore.ZombiesFallDamage",
            "ZombieLore.PlayerSpawnZombieRemoval"),
        Section("Advanced zombies",
            "ZombieConfig.PopulationMultiplier", "ZombieConfig.PopulationStartMultiplier",
            "ZombieConfig.PopulationPeakMultiplier", "ZombieConfig.PopulationPeakDay", "ZombieConfig.RespawnHours",
            "ZombieConfig.RespawnUnseenHours", "ZombieConfig.RespawnMultiplier", "ZombieConfig.RedistributeHours",
            "ZombieConfig.FollowSoundDistance", "ZombieConfig.RallyGroupSize", "ZombieConfig.RallyGroupSizeVariance",
            "ZombieConfig.RallyTravelDistance", "ZombieConfig.RallyGroupSeparation", "ZombieConfig.RallyGroupRadius",
            "ZombieConfig.ZombiesCountBeforeDelete"),
        Section("Time",
            "DayLength", "TimeSinceApo", "StartMonth", "StartDay", "StartTime", "StartYear", "NightLength"),
        Section("World",
            "WaterShutModifier", "ElecShutModifier", "WaterShut", "ElecShut", "AlarmDecay", "Alarm", "LockedHouses",
            "FireSpread", "AllowExteriorGenerator", "GeneratorTileRange", "GeneratorVerticalPowerRange",
            "FuelStationGasInfinite", "FuelStationGasMin", "FuelStationGasMax", "FuelStationGasEmptyChance",
            "LightBulbLifespan", "FoodRotSpeed", "FridgeFactor", "DaysForRottenFoodRemoval", "WorldItemRemovalList",
            "HoursForWorldItemRemoval", "ItemRemovalListBlacklistToggle", "MaximumFireFuelHours",
            "AlarmDecayModifier"),
        Section("Basements",
            "Basement.SpawnFrequency"),
        Section("Nature",
            "NightDarkness", "Temperature", "Rain", "MaxFogIntensity", "MaxRainFxIntensity", "ErosionSpeed",
            "ErosionDays", "FarmingSpeedNew", "CompostTime", "FishAbundance", "NatureAbundance", "PlantResilience",
            "FarmingAmountNew", "KillInsideCrops", "PlantGrowingSeasons", "PlaceDirtAboveground",
            "EnableSnowOnGround", "EnableTaintedWaterText", "MaximumRatIndex", "DaysUntilMaximumRatIndex",
            "ClayLakeChance", "ClayRiverChance", "Farming", "PlantAbundance"),
        Section("Loot",
            "HoursForLootRespawn", "SeenHoursPreventLootRespawn", "MaxItemsForLootRespawn",
            "ConstructionPreventsLootRespawn", "MaximumLooted", "DaysUntilMaximumLooted", "RuralLooted",
            "MaximumDiminishedLoot", "DaysUntilMaximumDiminishedLoot", "MaximumLootedBuildingRooms"),
        Section("Loot rarity",
            "FoodLootNew", "CannedFoodLootNew", "WeaponLootNew", "RangedWeaponLootNew", "AmmoLootNew",
            "MedicalLootNew", "SurvivalGearsLootNew", "MechanicsLootNew", "SkillBookLoot", "RecipeResourceLoot",
            "LiteratureLootNew", "ClothingLootNew", "ContainerLootNew", "KeyLootNew", "MediaLootNew",
            "MementoLootNew", "CookwareLootNew", "MaterialLootNew", "FarmingLootNew", "ToolLootNew", "OtherLootNew",
            "GeneratorSpawning", "LootItemRemovalList", "RemoveStoryLoot", "RemoveZombieLoot", "RollsMultiplier",
            "ZombiePopLootEffect", "InsaneLootFactor", "ExtremeLootFactor", "RareLootFactor", "NormalLootFactor",
            "CommonLootFactor", "AbundantLootFactor"),
        Section("Meta",
            "Helicopter", "MetaEvent", "SleepingEvent", "GeneratorFuelConsumption", "SurvivorHouseChance",
            "VehicleStoryChance", "ZoneStoryChance", "AnnotatedMapChance", "HoursForCorpseRemoval",
            "DecayingCorpseHealthImpact", "ZombieHealthImpact", "BloodLevel", "BloodSplatLifespanDays", "MaggotSpawn",
            "MetaKnowledge", "DayNightCycle", "ClimateCycle", "FogCycle", "ZombieLore.FenceThumpersRequired",
            "ZombieLore.FenceDamageMultiplier"),
        Section("Map",
            "Map.AllowWorldMap", "Map.AllowMiniMap", "Map.MapAllKnown", "Map.MapNeedsLight"),
        Section("Character",
            "StatsDecrease", "EndRegen", "Nutrition", "StarterKit", "CharacterFreePoints", "ConstructionBonusPoints",
            "InjurySeverity", "BoneFracture", "MuscleStrainFactor", "DiscomfortFactor", "WoundInfectionFactor",
            "ClothingDegradation", "NoBlackClothes", "RearVulnerability", "MultiHitZombies", "FirearmUseDamageChance",
            "FirearmNoiseMultiplier", "FirearmJamMultiplier", "FirearmMoodleMultiplier", "FirearmWeatherMultiplier",
            "FirearmHeadGearEffect", "AttackBlockMovements", "AllClothesUnlocked", "EnablePoisoning",
            "LiteratureCooldown", "NegativeTraitsPenalty", "MinutesPerPage", "LevelForDismantleXPCutoff",
            "LevelForMediaXPCutoff", "EasyClimbing", "SeeNotLearntRecipe"),
        Section("XP multipliers",
            "MultiplierConfig.Global", "MultiplierConfig.GlobalToggle", "MultiplierConfig.Fitness",
            "MultiplierConfig.Strength", "MultiplierConfig.Sprinting", "MultiplierConfig.Lightfoot",
            "MultiplierConfig.Nimble", "MultiplierConfig.Sneak", "MultiplierConfig.Axe", "MultiplierConfig.Blunt",
            "MultiplierConfig.SmallBlunt", "MultiplierConfig.LongBlade", "MultiplierConfig.SmallBlade",
            "MultiplierConfig.Spear", "MultiplierConfig.Maintenance", "MultiplierConfig.Farming",
            "MultiplierConfig.Husbandry", "MultiplierConfig.Woodwork", "MultiplierConfig.Carving",
            "MultiplierConfig.Cooking", "MultiplierConfig.Electricity", "MultiplierConfig.Doctor",
            "MultiplierConfig.FlintKnapping", "MultiplierConfig.Masonry", "MultiplierConfig.Mechanics",
            "MultiplierConfig.Blacksmith", "MultiplierConfig.Pottery", "MultiplierConfig.Tailoring",
            "MultiplierConfig.MetalWelding", "MultiplierConfig.Aiming", "MultiplierConfig.Reloading",
            "MultiplierConfig.Fishing", "MultiplierConfig.PlantScavenging", "MultiplierConfig.Tracking",
            "MultiplierConfig.Trapping", "MultiplierConfig.Butchering", "MultiplierConfig.Glassmaking"),
        Section("Vehicles",
            "EnableVehicles", "VehicleEasyUse", "RecentlySurvivorVehicles", "ZombieAttractionMultiplier",
            "CarSpawnRate", "ChanceHasGas", "InitialGas", "CarGasConsumption", "LockedCar", "CarGeneralCondition",
            "TrafficJam", "CarAlarm", "PlayerDamageFromCrash", "CarDamageOnImpact", "SirenShutoffHours",
            "DamageToPlayerFromHitByACar", "SirenEffectsZombies"),
        Section("Animals",
            "AnimalStatsModifier", "AnimalPregnancyTime", "AnimalEggHatch", "AnimalAgeModifier",
            "AnimalMilkIncModifier", "AnimalWoolIncModifier", "AnimalRanchChance", "AnimalGrassRegrowTime",
            "AnimalMetaPredator", "AnimalMatingSeason", "AnimalSoundAttractZombies", "AnimalTrackChance",
            "AnimalPathChance", "AnimalMetaStatsModifier"),
        Section("General",
            "VERSION"),
    ],
    [
        ("ZombieLore.", "Zombie lore"), ("ZombieConfig.", "Advanced zombies"), ("MultiplierConfig.", "XP multipliers"),
        ("Map.", "Map"), ("Basement.", "Basements"), ("Animal", "Animals"), ("Firearm", "Character"),
        ("Zombie", "Zombies"),
    ]);

    // A word inside a PascalCase key: an acronym run (RCON, PVP, ID), a capitalised or lowercase word, or a number
    // with its unit (3D).
    private static readonly Regex Word = new(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+[A-Z]*", RegexOptions.Compiled);

    // Lowercase words PZ spells in snake_case keys that read as acronyms.
    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal) { "ip", "id", "url", "xp" };

    /// <summary>
    /// The section a setting belongs in: the catalog's, else a name-prefix rule's, else (sandbox only) a section
    /// named after the mod table it sits in, else <see cref="OtherSection"/>.
    /// </summary>
    public static string SectionOf(PzConfigKind kind, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Catalog? catalog = CatalogOf(kind);
        if (catalog is null)
        {
            return OtherSection;
        }

        if (catalog.Sections.TryGetValue(path, out string? known))
        {
            return known;
        }

        foreach ((string prefix, string section) in catalog.Prefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return section;
            }
        }

        // A mod's sandbox options are written as a nested table named after the mod (B42 sandbox-options.txt
        // "option MyMod.Setting"), so the table name is the best grouping there is.
        int dot = path.IndexOf('.', StringComparison.Ordinal);
        return kind == PzConfigKind.SandboxVars && dot > 0 ? Humanize(path[..dot]) : OtherSection;
    }

    /// <summary>
    /// A sort key for a section: vanilla sections in the game's order, then any derived (mod) section, then
    /// <see cref="OtherSection"/> last. Derived sections share a rank, so a stable sort keeps their file order.
    /// </summary>
    public static int SectionRank(PzConfigKind kind, string section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section == OtherSection)
        {
            return int.MaxValue;
        }

        return CatalogOf(kind)?.Ranks.TryGetValue(section, out int rank) == true ? rank : DerivedRank;
    }

    /// <summary>A readable label from a setting's last path segment: <c>AdminSafehouse</c> → "Admin safehouse".</summary>
    public static string LabelOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int dot = path.LastIndexOf('.');
        return Humanize(dot < 0 ? path : path[(dot + 1)..]);
    }

    private static string Humanize(string key)
    {
        List<string> words = [];
        foreach (string part in key.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            words.AddRange(Word.Matches(part).Select(m => m.Value));
        }

        // B42 renamed several sandbox keys by appending "New" (FoodLootNew); the suffix means nothing to an operator.
        if (words.Count > 1 && words[^1] == "New")
        {
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count == 0)
        {
            return key;
        }

        for (int i = 0; i < words.Count; i++)
        {
            string word = words[i];
            bool keepCase = (word.Length > 1 && word.All(char.IsUpper)) || word.Any(char.IsDigit);
            if (Acronyms.Contains(word))
            {
                words[i] = word.ToUpperInvariant();
            }
            else if (!keepCase)
            {
                words[i] = i == 0
                    ? char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
                    : word.ToLowerInvariant();
            }
        }

        return string.Join(' ', words);
    }

    private static Catalog? CatalogOf(PzConfigKind kind) => kind switch
    {
        PzConfigKind.Ini => Ini,
        PzConfigKind.SandboxVars => SandboxVars,
        _ => null,
    };

    private static (string Name, string[] Keys) Section(string name, params string[] keys) => (name, keys);

    private sealed class Catalog
    {
        public Catalog((string Name, string[] Keys)[] sections, (string Prefix, string Section)[] prefixes)
        {
            Sections = new Dictionary<string, string>(StringComparer.Ordinal);
            Ranks = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < sections.Length; i++)
            {
                Ranks[sections[i].Name] = i;
                foreach (string key in sections[i].Keys)
                {
                    Sections.Add(key, sections[i].Name);
                }
            }

            Prefixes = prefixes;
        }

        public Dictionary<string, string> Sections { get; }

        public Dictionary<string, int> Ranks { get; }

        public (string Prefix, string Section)[] Prefixes { get; }
    }
}
