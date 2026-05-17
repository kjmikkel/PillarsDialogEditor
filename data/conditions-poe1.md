# Pillars of Eternity — Condition Reference

Extracted from `Assembly-CSharp/Conditionals.cs` (decompiled game assembly).  
131 conditions across 12 categories.

> **FullName format in conversation XML:**  
> `Boolean MethodName(Type1, Type2, ...)` where C# `string`→`String`, `int`→`Int32`,
> `float`→`Single`, nested types use `+` (e.g. `CharacterStats+SkillType`).

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
| Is In Stealth Mode *(DEPRECATED)* | `IsInStealth` | — |
| Is User In Stealth | `IsUserInStealth` | — |
| Has Been Detected | `HasBeenDetected` | Object (Guid) |
| Is User Being Perceived By | `IsUserBeingPerceivedBy` | Perceiver (Guid), Check Any Party Member (Boolean) |
| Is In Volume | `IsInVolume` | Object (Guid), Collider Object (Guid) |
| Command Line Arg Set | `CommandLineArg` | Arg (String) |
| Has PX1 | `HasPX1` | — |
| Has PX2 | `HasPX2` | — |
| Has PX4 | `HasPX4` | — |
| Has Package | `HasPackage` | Package (ProductConfiguration+Package) |
| Current Map Has Tag | `CurrentMapHasTag` | Tag (String) |
| Is Distance | `IsDistance` | Object A (Guid), Object B (Guid), Operator (Operator), Value (Single) |

---

## Time

| Display Name | Method | Parameters |
|---|---|---|
| Is Currently Daytime | `IsCurrentlyDaytime` | — |
| Is Currently Nighttime | `IsCurrentlyNighttime` | — |

---

## Difficulty

| Display Name | Method | Parameters |
|---|---|---|
| Is Easy | `IsEasy` | — |
| Is Normal | `IsNormal` | — |
| Is Hard | `IsHard` | — |
| Is Story Time | `IsStoryTime` | — |

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
| Is Armor Type Equipped In Party Slot | `IsArmorTypeEquippedInSlot` | Slot (Int32), Armor Type (Armor+Category) |

---

## RPG (Character Stats)

| Display Name | Method | Parameters |
|---|---|---|
| Is Attribute Score Value | `IsAttributeScoreValue` | Object (Guid), Attribute (CharacterStats+AttributeScoreType), Operator (Operator), Value (Int32) |
| Is Player Attribute Score Value | `IsPlayerAttributeScoreValue` | Attribute (CharacterStats+AttributeScoreType), Operator (Operator), Value (Int32) |
| Is Slot Attribute Score Value | `IsSlotAttributeScoreValue` | Slot (Int32), Attribute (CharacterStats+AttributeScoreType), Operator (Operator), Value (Int32) |
| Is Defense Value | `IsDefenseValue` | Object (Guid), Defense Type (CharacterStats+DefenseType), Operator (Operator), Value (Int32) |
| Is Player Defense Value | `IsPlayerDefenseValue` | Defense Type (CharacterStats+DefenseType), Operator (Operator), Value (Int32) |
| Is Skill Value *(PoE1 only)* | `IsSkillValue` | Object (Guid), Skill Type (CharacterStats+SkillType), Operator (Operator), Value (Int32) |
| Is Skill Value (Scaled) | `IsSkillValueScaled` | Object (Guid), Skill (CharacterStats+SkillType), Operator (Operator), Value (Int32), Scaler (DifficultyScaling+Scaler) |
| Is Party Skill Value Count *(PoE1 only)* | `IsPartySkillValueCount` | Skill (CharacterStats+SkillType), Skill Op (Operator), Skill Value (Int32), Party Op (Operator), Party Value (Int32) |
| Is Player Skill Value | `IsPlayerSkillValue` | Skill Type (CharacterStats+SkillType), Operator (Operator), Value (Int32) |
| Is Slot Skill Value | `IsSlotSkillValue` | Slot (Int32), Skill Type (CharacterStats+SkillType), Operator (Operator), Value (Int32) |
| Is Player Character Using S.I. | `IsPlayerCharacterUsingSI` | — |
| Is Player Character Skill Check 0 | `IsPlayerCharacterSkillCheckZero` | — |
| Is Player Character Skill Check X | `IsPlayerCharacterSkillCheckX` | Check Index (Int32) |
| Is Party Slot Skill Check X | `IsPartySlotSkillCheckX` | Slot (Int32), Check Index (Int32) |
| Is Character Skill Check X | `IsCharacterSkillCheckX` | Object (Guid), Check Index (Int32) |
| Is Class | `IsClass` | Object (Guid), Class (CharacterStats+Class) |
| Is Player Class | `IsPlayerClass` | Class (CharacterStats+Class) |
| Is Race | `IsRace` | Object (Guid), Race (CharacterStats+Race) |
| Is Player Race | `IsPlayerRace` | Race (CharacterStats+Race) |
| Is Subrace | `IsSubrace` | Object (Guid), Subrace (CharacterStats+Subrace) |
| Is Player Subrace | `IsPlayerSubrace` | Subrace (CharacterStats+Subrace) |
| Is Gender | `IsGender` | Object (Guid), Gender (Gender) |
| Is Player Gender | `IsPlayerGender` | Gender (Gender) |
| Is Slot Gender | `IsSlotGender` | Slot (Int32), Gender (Gender) |
| Has Talent or Ability | `HasTalentOrAbility` | Object (Guid), Ability Name (String) |
| Is Deity | `IsDeity` | Object (Guid), Deity (Religion+Deity) |
| Is Paladin Order | `IsPaladinOrder` | Object (Guid), Order (Religion+PaladinOrder) |
| Is Culture | `IsCulture` | Object (Guid), Culture (CharacterStats+Culture) |
| Is Player Culture | `IsPlayerCulture` | Culture (CharacterStats+Culture) |
| Is Background | `IsBackground` | Object (Guid), Background (CharacterStats+Background) |
| Is Player Background | `IsPlayerBackground` | Background (CharacterStats+Background) |
| Any Party Member Can Use Ability | `AnyPartyMemberCanUseAbility` | Ability Name String ID (Int32) |
| Character Can Use Ability | `CharacterCanUseAbility` | Object (Guid), Ability Name String ID (Int32) |
| Slot Can Use Ability | `SlotCanUseAbility` | Slot (Int32), Ability Name String ID (Int32) |
| Is Player Level | `IsPlayerLevel` | Operator (Operator), Target Level (Int32) |

---

## Stronghold

| Display Name | Method | Parameters |
|---|---|---|
| Current Map Is Stronghold | `CurrentMapIsStronghold` | — |
| Can Add Prisoner | `CanAddPrisoner` | — |
| Stronghold Has Prisoner (by Guid) | `StrongholdHasPrisoner` | Object (Guid) |
| Stronghold Has Prisoner (by Name) | `StrongholdHasPrisoner` | Prisoner Name (String) |
| Stronghold Has Upgrade | `StrongholdHasUpgrade` | Upgrade Type (StrongholdUpgrade+Type) |
| Stronghold Is Building Upgrade | `StrongholdIsBuildingUpgrade` | Upgrade Type (StrongholdUpgrade+Type) |
| Stronghold Is Companion Available | `StrongholdIsCompanionAvaliable` | Object (Guid) |
| Stronghold Is Security Value | `StrongholdIsSecurityValue` | Operator (Operator), Value (Int32) |
| Stronghold Is Prestige Value | `StrongholdIsPrestigeValue` | Operator (Operator), Value (Int32) |
| Stronghold Is Visitor Dead | `StrongholdIsVisitorDead` | Tag (String) |
| Stronghold Is Visitor Present | `StrongholdIsVisitorPresent` | Tag (String) |

---

## Minigame

| Display Name | Method | Parameters |
|---|---|---|
| Dozens Game Player Result Is | `DozensGamePlayerResultIs` | Result (Dozens+Result) |
| Dozens Game Opponent Result Is | `DozensGameOpponentResultIs` | Result (Dozens+Result) |
| Dozens Game Is Player Winning | `DozensGamePlayerWinning` | — |
| Dozens Game Is Opponent Winning | `DozensGameOpponentWinning` | — |
| Dozens Game Is Tied | `DozensGameIsTied` | — |
| Orlan Game Is Player Winning | `OrlanGamePlayerWinning` | — |
| Orlan Game Player Result Is | `OrlanGamePlayerResultIs` | Result (OrlansHead+Result) |
| Orlan Game Is Opponent Winning | `OrlanGameOpponentWinning` | — |
| Orlan Game Opponent Result Is | `OrlanGameOpponentResultIs` | Result (OrlansHead+Result) |
| Orlan Game Is Tied | `OrlanGameIsTied` | — |
| Orlan Game Round Count Is | `OrlanGameRoundCountIs` | Operator (Operator), Value (Int32) |

---

## OCL (Containers / Doors)

| Display Name | Method | Parameters |
|---|---|---|
| Is OCL In A State | `IsOCLInState` | Object (Guid), State (OCL+State) |

---

## Misc

| Display Name | Method | Parameters |
|---|---|---|
| Is Distance | `IsDistance` | Object A (Guid), Object B (Guid), Operator (Operator), Value (Single) |

---

## Operator Enum Values

Used in comparison parameters: `EqualTo`, `NotEqualTo`, `GreaterThan`, `LessThan`, `GreaterThanOrEqualTo`, `LessThanOrEqualTo`

## BrowserType Values (PoE1)

| BrowserType | Meaning |
|---|---|
| `ObjectGuid` | In-scene game object selected by GUID |
| `GlobalVariable` | Named global flag/counter |
| `Conversation` | Conversation file name |
| `Quest` | Quest file name |
