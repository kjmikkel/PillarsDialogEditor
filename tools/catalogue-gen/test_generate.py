import generate

SCRIPT = r'''
[Script("Add Reputation", "Scripts\\Faction")]
[ScriptParam0("Faction", "Faction to modify.", "", "e7b2bb2a-99a8-41cb-b0f2-6f1fb973d5ac", BrowserType.GameData)]
[ScriptParam1("Axis", "Good vs. Bad action", Axis.Positive)]
[ScriptParam2("Strength", "Severity of the change", "71c858fe-7c4b-432a-a105-c518319eaed7", "32e5c672-85db-423f-b363-71c8a08674fc", BrowserType.GameData)]
public static void ReputationAddPoints(Guid factionGuid, Axis axis, Guid strengthGuid)
{
}
'''


def test_parses_method_header():
    e = generate.parse_source(SCRIPT, "script")[0]
    assert e["methodName"] == "ReputationAddPoints"
    assert e["fullName"] == "Void ReputationAddPoints(Guid, Axis, Guid)"
    assert e["displayName"] == "Add Reputation"
    assert e["category"] == "Faction"
    names = [p["name"] for p in e["parameters"]]
    assert names == ["Faction", "Axis", "Strength"]
    assert e["parameters"][0]["description"] == "Faction to modify."


def test_condition_uses_conditionalscript_attr():
    COND = r'''
    [ConditionalScript("Is Reputation", "Conditionals\\Faction")]
    [ScriptParam0("Faction", "Faction to modify.", "", "e7b2bb2a-99a8-41cb-b0f2-6f1fb973d5ac", Scripts.BrowserType.GameData)]
    [ScriptParam1("Rank Type", "type", RankType.Good)]
    [ScriptParam2("Rank", "r", 0)]
    [ScriptParam3("Operator", "op", Operator.EqualTo)]
    public static bool IsReputation(Guid factionGuid, RankType type, int rankValue, Operator comparisonOperator)
    { }
    '''
    e = generate.parse_source(COND, "condition")[0]
    assert e["fullName"] == "Boolean IsReputation(Guid, RankType, Int32, Operator)"
    assert e["displayName"] == "Is Reputation"


# ── Task 2: enums / defaults / type ────────────────────────────────────────
ENUMS = r'''
public enum Axis { Positive, Negative }
public enum RankType { Good = 0, Bad = 1 }
'''


def test_parse_enums():
    m = generate.parse_enums(ENUMS)
    assert m["Axis"] == ["Positive", "Negative"]
    assert m["RankType"] == ["Good", "Bad"]


def test_enum_param_gets_options_and_default():
    enums = generate.parse_enums(ENUMS)
    e = generate.parse_source(SCRIPT, "script", enums=enums)[0]
    axis = e["parameters"][1]
    assert axis["type"] == "Enum:Axis"
    assert axis["options"] == ["Positive", "Negative"]
    assert axis["default"] == "Positive"


def test_value_param_type_and_default():
    enums = generate.parse_enums(ENUMS)
    src = r'''
    [Script("Give Player Money", "Scripts\\Items")]
    [ScriptParam0("Amount", "n", 5)]
    public static void GivePlayerMoney(int amount) { }
    '''
    e = generate.parse_source(src, "script", enums=enums)[0]
    amt = e["parameters"][0]
    assert amt["type"] == "Int32"
    assert amt["default"] == "5"


# ── Task 3: BrowserType + GameData $type -> lookupKind ──────────────────────
def test_parse_datatype_index():
    src = r'''
    public class FactionGameData : GameDataObject
    {
        public const string DataTypeID = "e7b2bb2a-99a8-41cb-b0f2-6f1fb973d5ac";
    }
    '''
    m = generate.parse_datatype_index(src)
    assert m["e7b2bb2a-99a8-41cb-b0f2-6f1fb973d5ac"] == "FactionGameData"


def test_gamedata_param_resolves_via_datatype_id():
    # The 2nd attribute GUID is the DataTypeID (type identifier), not an instance.
    dt = {
        "e7b2bb2a-99a8-41cb-b0f2-6f1fb973d5ac": "FactionGameData",
        "32e5c672-85db-423f-b363-71c8a08674fc": "ChangeStrengthGameData",
    }
    enums = generate.parse_enums(ENUMS)
    e = generate.parse_source(SCRIPT, "script", enums=enums, datatype_index=dt)[0]
    assert e["parameters"][0]["lookupKind"] == "Faction"
    assert e["parameters"][0]["type"] == "GameData"
    assert e["parameters"][2]["lookupKind"] == "ChangeStrength"


def test_gamedata_4arg_falls_back_to_instance_index():
    # 4-arg form: (name, desc, defaultInstanceGuid, browser) — no DataTypeID.
    src = r'''
    [Script("X","Scripts\\Misc")]
    [ScriptParam0("Thing","t","5325a7f1-0292-41bb-a223-2c84c005779a", BrowserType.GameData)]
    public static void X(Guid thing) { }
    '''
    e = generate.parse_source(src, "script", enums={},
                              guid_index={"5325a7f1-0292-41bb-a223-2c84c005779a": "FactionGameData"})[0]
    assert e["parameters"][0]["lookupKind"] == "Faction"


def test_objectguid_maps_to_speaker():
    src = r'''
    [Script("X","Scripts\\Misc")]
    [ScriptParam0("Who","w","", BrowserType.ObjectGuid)]
    public static void X(Guid who) { }
    '''
    e = generate.parse_source(src, "script", enums={}, guid_index={})[0]
    assert e["parameters"][0]["lookupKind"] == "Speaker"
    assert e["parameters"][0]["type"] == "ObjectGuid"


def test_unresolvable_gamedata_is_generic():
    src = r'''
    [Script("X","Scripts\\Misc")]
    [ScriptParam0("D","d","", BrowserType.GameData)]
    public static void X(Guid d) { }
    '''
    e = generate.parse_source(src, "script", enums={}, guid_index={})[0]
    assert e["parameters"][0]["lookupKind"] == "GameData"


# ── Task 4: merge games ─────────────────────────────────────────────────────
def _entry(fn, games):
    return {"fullName": fn, "games": games, "methodName": fn.split()[1].split("(")[0],
            "displayName": "D", "category": "C", "description": "", "parameters": []}


def test_merge_unions_games_for_same_signature():
    a = [_entry("Void F(Guid)", ["poe1"])]
    b = [_entry("Void F(Guid)", ["poe2"]), _entry("Void G()", ["poe2"])]
    merged = generate.merge_games(a, b)
    byfn = {e["fullName"]: e for e in merged}
    assert byfn["Void F(Guid)"]["games"] == ["poe1", "poe2"]
    assert byfn["Void G()"]["games"] == ["poe2"]


def test_param_kind_override_applies():
    src = r'''
    [ConditionalScript("Reputation Rank Equals", "Conditionals\\Faction")]
    [ScriptParam0("Object", "Object to check.", "", Scripts.BrowserType.ObjectGuid)]
    [ScriptParam1("Axis", "a", "Positive")]
    [ScriptParam2("Ranks", "r", "0")]
    public static bool ReputationRankEquals(Guid objectGuid, Axis axis, int rank) { }
    '''
    e = generate.parse_source(src, "condition", enums={})[0]
    assert e["parameters"][0]["lookupKind"] == "Faction"
    assert e["parameters"][0]["type"] == "GameData"


if __name__ == "__main__":
    fns = [v for k, v in sorted(globals().items()) if k.startswith("test_") and callable(v)]
    for fn in fns:
        fn()
        print("ok", fn.__name__)
    print(f"{len(fns)} passed")
