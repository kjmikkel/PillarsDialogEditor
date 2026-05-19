# Pillars of Eternity II: Deadfire — Condition Reference

Extracted from `Assembly-CSharp/Game/Conditionals.cs` (decompiled game assembly).  
287 conditions across 15+ categories.

> **Key differences from PoE1:**  
> - `BrowserType.GameData` params carry a type-hint GUID identifying the asset type  
> - Many PoE2 conditionals share PoE1 names but may have different signatures  
> - Ship, Crew, World Map, and Time categories are PoE2-exclusive

---

## General

| Display Name | Method | Parameters |
|---|---|---|
| Are Guids Same Object | `AreGuidsSameObject` | Object 1 (Guid), Object 2 (Guid) |
| Is Active | `IsActive` | Object (Guid) |
| Is Companion Active In Party | `IsCompanionActiveInParty` | Companion (Guid) |
| Is Any Companion Active In Party | `IsAnyCompanionActiveInParty` | — |
| Is Slot Active | `IsSlotActive` | Slot (Int32) |
| Is Player In Slot | `IsPlayerInSlot` | Slot (Int32) |
| Is Character In Slot | `IsCharacterInSlot` | Companion (Guid), Slot (Int32) |
| Is In Combat | `IsInCombat` | — |
| Has Combat Time Elapsed | `HasCombatTimeElapsed` | Seconds (Single) |
| Is User In Stealth | `IsUserInStealth` | — |
| Has Been Detected | `HasBeenDetected` | Object (Guid) |
| Is User Being Perceived By | `IsUserBeingPerceivedBy` | Perceiver (Guid), Check Any Party Member (Boolean) |
| Is In Volume | `IsInVolume` | Object (Guid), Collider Object (Guid) |
| Command Line Arg Set | `CommandLineArg` | Arg (String) |
| Current Map Has Tag | `CurrentMapHasTag` | Tag (String) |
| Is Distance | `IsDistance` | Object A (Guid), Object B (Guid), Operator (Operator), Value (Single) |

---

## Globals

| Display Name | Method | Parameters |
|---|---|---|
| Is Global Value | `IsGlobalValue` | Tag (GlobalVariable), Operator (Operator), Value (Int32) |
| Compare Globals | `CompareGlobals` | Global 1 (GlobalVariable), Operator (Operator), Global 2 (GlobalVariable) |

---

## Conversation

| Display Name | Method | Parameters |
|---|---|---|
| Has Conversation Node Been Played | `HasConversationNodeBeenPlayed` | Conversation (Conversation), Node ID (Int32) |

---

## Quest

| Display Name | Method | Parameters |
|---|---|---|
| Is Quest On Node | `IsQuestOnNode` | Quest Name (Quest), Node ID (Int32) |
| Is Quest Event Triggered | `IsQuestEventTriggered` | Quest Name (Quest), Event ID (Int32) |
| Is Quest Addendum Triggered | `IsQuestAddendumTriggered` | Quest Name (Quest), Addendum ID (Int32) |
| Is Quest End State Triggered | `IsQuestEndStateTriggered` | Quest Name (Quest), End State ID (Int32) |
| Is Quest Failed | `IsQuestFailed` | Quest Name (Quest) |

---

## Faction & Reputation

| Display Name | Method | Parameters |
|---|---|---|
| Is Team Relationship | `IsTeamRelationship` | Team A (String), Team B (String), Relationship (Faction+Relationship) |
| Reputation Rank Equals | `ReputationRankEquals` | Object (Guid), Axis (Reputation+Axis), Rank (Int32) |
| Tagged Reputation Rank Equals | `ReputationRankByTagEquals` | Faction Name (FactionName), Axis (Reputation+Axis), Rank (Int32) |
| Reputation Rank Greater or Equal | `ReputationRankGreater` | Object (Guid), Axis (Reputation+Axis), Rank (Int32) |
| Tagged Reputation Rank Greater or Equal | `ReputationTagRankGreater` | Faction Name (FactionName), Axis (Reputation+Axis), Rank (Int32) |
| Disposition Equal | `DispositionEqual` | Axis (Disposition+Axis), Rank (Disposition+Rank) |
| Disposition Greater or Equal | `DispositionGreaterOrEqual` | Axis (Disposition+Axis), Rank (Disposition+Rank) |

---

## Health

| Display Name | Method | Parameters |
|---|---|---|
| Is Health Value | `IsHealthValue` | Object (Guid), Operator (Operator), Value (Single) |
| Is Health Percentage | `IsHealthPercentage` | Object (Guid), Operator (Operator), Percentage (Single) |
| Is Stamina Value | `IsStaminaValue` | Object (Guid), Operator (Operator), Value (Single) |
| Is Stamina Percentage | `IsStaminaPercentage` | Object (Guid), Operator (Operator), Percentage (Single) |

---

## Items

| Display Name | Method | Parameters |
|---|---|---|
| Is Item Count | `IsItemCount` | Item Name (String), Operator (Operator), Count (Int32) |
| Is Item Equipped | `IsItemEquipped` | Object (Guid), Item Name (String) |
| Is Item Equipped On Player | `IsItemEquippedOnPlayer` | Item Name (String) |
| Has Money | `HasMoney` | Operator (Operator), Amount (Int32) |
| Object Has Item Count | `ObjectHasItemCount` | Object (Guid), Item Name (String), Operator (Operator), Count (Int32) |
| Is Weapon Type Equipped In Primary Slot | `IsWeaponTypeEquippedInPrimarySlot` | Object (Guid), Weapon Type (WeaponSpecializationData+WeaponType) |
| Is Armor Type Equipped | `IsArmorTypeEquipped` | Object (Guid), Armor Type (Armor+Category) |

---

## RPG (Character Stats)

| Display Name | Method | Parameters |
|---|---|---|
| Is Attribute Score Value | `IsAttributeScoreValue` | Object (Guid), Attribute (CharacterStats+AttributeScoreType), Operator (Operator), Value (Int32) |
| Is Player Attribute Score Value | `IsPlayerAttributeScoreValue` | Attribute (CharacterStats+AttributeScoreType), Operator (Operator), Value (Int32) |
| Is Defense Value | `IsDefenseValue` | Object (Guid), Defense Type (CharacterStats+DefenseType), Operator (Operator), Value (Int32) |
| Is Player Defense Value | `IsPlayerDefenseValue` | Defense Type (CharacterStats+DefenseType), Operator (Operator), Value (Int32) |
| Is Party Skill Value Count *(PoE2 — Guid-based)* | `IsPartySkillValueCount` | Skill Type (GameData Guid), Skill Op (Operator), Skill Value (Int32), Party Op (Operator), Party Value (Int32), Hide Notification (Boolean) |
| Is Skill Value *(PoE2 — Guid-based)* | `IsSkillValue` | Object (Guid), Skill Type **(GameData Guid)**, Operator (Operator), Value (Int32), Is Assisted (Boolean), Hide Notification If Failure (Boolean) |
| Is Skill Value (Scaled) *(PoE2 — Guid-based)* | `IsSkillValueScaled` | Object (Guid), Skill Type (GameData Guid), Operator (Operator), Value (Int32), Is Assisted (Boolean), Scaler, Hide Notification |
| Is Player Skill Value | `IsPlayerSkillValue` | Skill Type (CharacterStats+SkillType), Operator (Operator), Value (Int32) |
| Is Level | `IsLevel` | Object (Guid), Operator (Operator), Target Level (Int32) |
| Is Scaled Level | `IsScaledLevel` | Object (Guid), Operator (Operator), Target Level (Int32) |
| Is Level Compared to Other | `IsLevelComparedToOther` | Object (Guid), Object 2 (Guid), Operator (Operator) |
| Is Scaled Level Compared to Other | `IsScaledLevelComparedToOther` | Object (Guid), Object 2 (Guid), Operator (Operator) |
| Is Class | `IsClass` | Object (Guid), Class (CharacterStats+Class) |
| Is Player Class | `IsPlayerClass` | Class (CharacterStats+Class) |
| Is Race | `IsRace` | Object (Guid), Race (CharacterStats+Race) |
| Is Player Race | `IsPlayerRace` | Race (CharacterStats+Race) |
| Is Subrace | `IsSubrace` | Object (Guid), Subrace (CharacterStats+Subrace) |
| Is Player Subrace | `IsPlayerSubrace` | Subrace (CharacterStats+Subrace) |
| Is Gender | `IsGender` | Object (Guid), Gender (Gender) |
| Is Player Gender | `IsPlayerGender` | Gender (Gender) |
| Is Slot Gender | `IsSlotGender` | Slot (Int32), Gender (Gender) |
| Is Deity | `IsDeity` | Object (Guid), Deity (Religion+Deity) |
| Is Paladin Order | `IsPaladinOrder` | Object (Guid), Order (Religion+PaladinOrder) |
| Is Culture | `IsCulture` | Object (Guid), Culture (CharacterStats+Culture) |
| Is Player Culture | `IsPlayerCulture` | Culture (CharacterStats+Culture) |
| Is Background | `IsBackground` | Object (Guid), Background (CharacterStats+Background) |
| Is Player Background | `IsPlayerBackground` | Background (CharacterStats+Background) |
| Can Use Phrase | `CanUsePhrase` | Object (Guid), Phrase (GameData) |
| Party Has Ability, Talent, or Phrase | `PartyHasAbility` | Unlockable (GameData) |
| Party Has Discovered Ability | `PartyHasDiscoveredAbility` | Ability (GameData) |
| Is Status Effect Count | `IsStatusEffectCount` | Object (Guid), Status Effect (GameData), Operator (Operator), Count (Int32) |
| Is Status Effect Type Count | `IsStatusEffectTypeCount` | Object (Guid), Effect Type (StatusEffectType), Operator (Operator), Count (Int32) |
| Is Summons Count | `IsSummonsCount` | Object (Guid), Operator (Operator), Count (Int32) |
| Has Keyword | `HasKeyword` | Object (Guid), Keyword (GameData) |
| Has Status Effect With Keyword | `HasStatusEffectWithKeyword` | Object (Guid), Keyword (GameData) |
| Is Accrued Resource Below Max | `IsAccruedResourceBelowMax` | Object (Guid), Class (GameData) |
| Is Creature Type | `IsCreatureType` | Object (Guid), Creature Type (GameData) |
| Is Chanting | `IsChanting` | Object (Guid) |
| Is Chanter Phrase Count | `IsChanterPhraseCount` | Object (Guid), Operator (Operator), Count (Int32) |
| Is Animal Companion Within Range | `IsAnimalCompanionWithinRange` | Object (Guid), Operator (Operator), Range (Single) |
| Is Animal Companion Alive | `IsAnimalCompanionAlive` | Object (Guid) |
| Has Animal Companion | `HasAnimalCompanion` | Object (Guid) |
| Is Class Accrued Resource Count | `IsClassAccruedResourceCount` | Object (Guid), Class (GameData), Operator (Operator), Count (Int32) |
| Is Class Power Pool Count | `IsClassPowerPoolCount` | Object (Guid), Class (GameData), Operator (Operator), Count (Int32) |

---

## Difficulty

| Display Name | Method | Parameters |
|---|---|---|
| Is Easy | `IsEasy` | — |
| Is Normal | `IsNormal` | — |
| Is Hard | `IsHard` | — |
| Is Story Time | `IsStoryTime` | — |

---

## Time

| Display Name | Method | Parameters |
|---|---|---|
| Is Currently Daytime | `IsCurrentlyDaytime` | — |
| Is Currently Nighttime | `IsCurrentlyNighttime` | — |
| Is Month and Day | `IsMonthAndDay` | Month (Int32, 0-based), Day (Int32, 1-based) |
| Is Current Time of Day | `IsCurrentTime` | Operator (Operator), Time in Hours (Single) |
| Is Schedule Time | `IsScheudleTime` | Schedule (GameData), Time Slice Index (Int32) |
| Is Character Schedule Time | `IsCharacterScheduleTime` | Schedule Character (Guid), Time Slice Name (String) |

---

## Weather

| Display Name | Method | Parameters |
|---|---|---|
| Is Sunny | `IsSunny` | — |
| Is Raining | `IsRaining` | — |

---

## Ships

| Display Name | Method | Parameters |
|---|---|---|
| Is Player Ship Attribute | `IsPlayerShipAttribute` | Attribute (ShipAttributeType), Operator (Operator), Value (Int32) |
| Is Target Ship Attribute | `IsTargetShipAttribute` | Ship (Guid), Attribute (ShipAttributeType), Operator (Operator), Value (Int32) |
| Is Ship Attribute | `IsShipAttribute` | Ship Data (GameData), Attribute (ShipAttributeType), Operator (Operator), Value (Int32) |
| Player Ship Has Active Upgrade | `PlayerShipHasActiveUpgrade` | Upgrade (GameData) |
| Player Ship Has Upgrade In Slot | `PlayerShipHasUpgradeInSlot` | Upgrade (GameData), Slot Type (ShipUpgradeSlotType), Slot Index (Int32) |
| Is Supply Count | `IsSupplyCount` | Supply Type (ShipSupplyType), Operator (Operator), Value (Int32) |
| Is Ship Food Count | `IsShipFoodCount` | Operator (Operator), Value (Int32) |
| Is Ship Drink Count | `IsShipDrinkCount` | Operator (Operator), Value (Int32) |
| Is Current Hull Health Ratio | `IsShipHullHealthRatio` | Ship (ShipDuelParticipant), Operator (Operator), Ratio (Single) |
| Is Current Sail Health Ratio | `IsShipSailHealthRatio` | Ship (ShipDuelParticipant), Operator (Operator), Ratio (Single) |
| Is Injured Crew Ratio | `IsShipInjuredCrewRatio` | Ship (ShipDuelParticipant), Operator (Operator), Ratio (Single) |

---

## Ship Crew

| Display Name | Method | Parameters |
|---|---|---|
| Is Morale Status | `IsMoraleStatus` | Morale State (MoraleStateType) |
| Is Morale Number | `IsMoraleNumber` | Operator (Operator), Value (Int32) |
| Has Any Crew in Job | `HasAnyCrewJob` | Job (ShipCrewJobType) |
| Has Crew Speaker in Job | `HasCrewJob` | Job (ShipCrewJobType) |
| Is Any Crew in Job Count | `HasAnyCrewJobAmount` | Job (ShipCrewJobType), Operator (Operator), Count (Int32) |
| Is Crew Speaker in Job Count | `HasCrewJobAmount` | Job (ShipCrewJobType), Operator (Operator), Count (Int32) |
| Is Any Crew Count | `IsAnyCrewCount` | Operator (Operator), Count (Int32) |
| Is Crew Speaker Count | `IsCrewCount` | Operator (Operator), Count (Int32) |
| Is Crew Job Ranks | `IsCrewJobRanks` | Job (ShipCrewJobType), Operator (Operator), Rank Count (Int32) |
| Crew Member Has Personality | `CrewMemberHasPersonality` | Crew Member (Guid), Personality (GameData) |
| Is Crew Member Active (on player ship) | `IsPlayerCrewMemberActive` | Crew Member (GameData) |

---

## Ship Duel (Naval Combat)

| Display Name | Method | Parameters |
|---|---|---|
| Is In Combat Encounter | `IsInCombatEncounter` | — |
| Whose Turn | `ShipDuelIsTurn` | Participant (ShipDuelParticipant) |
| Is First Turn | `ShipDuelIsFirstTurn` | — |
| Can Flee | `CanFlee` | Actor (ShipDuelParticipant) |
| Is Relative Bearing | `ShipDuelIsRelativeBearing` | From (ShipDuelParticipant), Bearing (ShipCombatRelativeBearing) |
| Opponent Has Captain Personality | `HasCaptainPersonality` | Personality (GameData) |
| Is Ship Distance | `IsShipDistance` | Operator (Operator), Distance (Int32) |
| Full Sail Will Ram | `FullSailWillRam` | Actor (ShipDuelParticipant) |
| Half Sail Will Ram | `HalfSailWillRam` | Actor (ShipDuelParticipant) |
| Is At Ship Retreat Distance | `IsAtShipRetreatDistance` | — |
| Is Ship Type | `IsShipType` | Target (ShipDuelParticipant), Ship Type (ShipType) |
| Is Ship Death Type | `IsShipDeathType` | Target (ShipDuelParticipant), Death Type (ShipDeathType) |
| Is Current Hull Health Value | `IsCurrentHullHealthValue` | Target (ShipDuelParticipant), Operator (Operator), Value (Int32) |
| Is Current Sail Health Value | `IsCurrentSailHealthValue` | Target (ShipDuelParticipant), Operator (Operator), Value (Int32) |
| Has Ship Duel Advantage | `HasShipDuelAdvantage` | Target (ShipDuelParticipant) |
| Has Ongoing Action | `HasOngoingAction` | Target (ShipDuelParticipant) |
| Has Ongoing Action Of Type | `HasOngoingActionOfType` | Target (ShipDuelParticipant), Action (ShipDuelActionType) |
| Is Action Valid | `IsActionValid` | Target (ShipDuelParticipant), Action (ShipDuelActionType) |
| Is Last Volley Hit Count | `IsLastVolleyHitCount` | Target (ShipDuelParticipant), Operator (Operator), Count (Int32) |
| Is Last Volley Miss Count | `IsLastVolleyMissCount` | Target (ShipDuelParticipant), Operator (Operator), Count (Int32) |
| Is Last Volley Shot Count | `IsLastVolleyShotCount` | Target (ShipDuelParticipant), Operator (Operator), Count (Int32) |
| Is Last Volley Damage Amount | `IsLastVolleyDamageAmount` | Target (ShipDuelParticipant), Damage Type (ShipDuelDamageType), Operator (Operator), Value (Int32) |
| Is Ship Exploded | `IsShipExploded` | Target (ShipDuelParticipant) |
| AI Action Is | `ShipDuelAIActionIs` | Action (ShipDuelActionType) |

---

## Targeting Filter (AI / Combat)

| Display Name | Method | Parameters |
|---|---|---|
| Is Engaged By Anyone | `IsEngagedByAnyone` | Object (Guid) |
| Is Engaged By Count | `IsEngagedByCount` | Object (Guid), Operator (Operator), Count (Int32) |
| Is Engaging Anyone | `IsEngagingAnyone` | Object (Guid) |
| Is Engaging Count | `IsEngagingCount` | Object (Guid), Operator (Operator), Count (Int32) |
| Is Threatened By Anyone | `IsThreatenedByAnyone` | Object (Guid) |
| Is Threatened By Count | `IsThreatenedByCount` | Object (Guid), Operator (Operator), Count (Int32) |
| Is Threatening Anyone | `IsThreateningAnyone` | Object (Guid) |
| Is Threatening Count | `IsThreateningCount` | Object (Guid), Operator (Operator), Count (Int32) |
| Has Summoned Weapon | `HasSummonedWeapon` | Object (Guid) |
| Is Targeted By Attacker's Animal Companion | `IsEngagedByAttackersAnimalCompanion` | Object (Guid) |
| Is Distance To Target | `IsDistanceToTarget` | Target (Guid), Operator (Operator), Distance (Single) |
| Is In Attack Range | `IsInAttackRange` | Object (Guid) |
| Has Active Ability | `HasActiveAbility` | Object (Guid), Ability (GameData) |
| Has Affliction Of Type | `HasAfflictionOfType` | Object (Guid), Affliction Type (GameData) |
| Has Affliction Of Type with Duration | `HasAfflictionOfTypeWithDuration` | Object (Guid), Affliction Type (GameData), Operator (Operator), Duration (Single) |
| Has Status Effect | `HasStatusEffect` | Object (Guid), Status Effect (GameData) |
| Has Status Effect From Source | `HasStatusEffectFromSource` | Object (Guid), Status Effect (GameData), Source (Guid) |
| Has Status Effect Type | `HasStatusEffectType` | Object (Guid), Effect Type (StatusEffectType) |
| Has Status Effect Type From Source | `HasStatusEffectTypeFromSource` | Object (Guid), Effect Type (StatusEffectType), Source (Guid) |
| Has Hostile Effect | `HasHostileEffect` | Object (Guid) |
| Has Hostile Effect With Duration | `HasHostileEffectWithDuration` | Object (Guid), Operator (Operator), Duration (Single) |
| Has Beneficial Effect With Duration | `HasBeneficialEffectWithDuration` | Object (Guid), Operator (Operator), Duration (Single) |
| Is Stunned | `IsStunned` | Object (Guid) |
| Is Ally Count | `IsAllyCount` | Object (Guid), Range (Single), Operator (Operator), Count (Int32) |
| Is Enemy Count | `IsEnemyCount` | Object (Guid), Range (Single), Operator (Operator), Count (Int32) |
| Is Number Of Targets In AOE | `IsNumberOfTargetsInAOE` | Object (Guid), Target Type (AutoTargetingType), Operator (Operator), Count (Int32) |
| Is Cast Time Remaining | `IsCastTimeRemaining` | Object (Guid), Operator (Operator), Time (Single) |

---

## World Map

| Display Name | Method | Parameters |
|---|---|---|
| Is Uncharted Discoveries | `IsUnchartedDiscoveries` | Count (Int32), Operator (Operator) |
| Is Map Visibility | `IsMapVisibility` | Map Type (MapType), Visibility (MapVisibilityType) |
| Is Embark Enabled | `IsEmbarkEnabled` | — |
| Is World Map Transit Mode | `IsWorldMapTransitMode` | Mode (WorldMapTransitMode) |
| Is Sea Shanties Enabled | `IsSeaShantiesEnabled` | — |
| Has Player Named Feature | `HasPlayerNamedFeature` | Feature (GameData) |

---

## BrowserType Values (PoE2)

| BrowserType | Meaning |
|---|---|
| `ObjectGuid` | In-scene game object selected by GUID |
| `GlobalVariable` | Named global flag/counter |
| `Conversation` | Conversation file name |
| `Quest` | Quest file name |
| `GameData` | Asset selected by GUID; type identified by a hint GUID embedded in the attribute |

## Operator Enum Values

`EqualTo`, `NotEqualTo`, `GreaterThan`, `LessThan`, `GreaterThanOrEqualTo`, `LessThanOrEqualTo`
