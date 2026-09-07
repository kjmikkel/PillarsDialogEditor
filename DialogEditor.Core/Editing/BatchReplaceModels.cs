using DialogEditor.Core.GameData;

namespace DialogEditor.Core.Editing;

// NOTE: there is deliberately no "link choice text" toggle here. NodeLink's
// QuestionNodeTextDisplay looks like a free-text field but is an enum with three
// legal values (ShowOnce/Always/Never); a substring replace inside one of those
// names produces an invalid value that PoE2 silently collapses to ShowOnce and
// PoE1 writes verbatim into the XML. It is edited through the node detail
// dropdown, never by search-and-replace. See issue #24.
public record BatchReplaceQuery(
    string SearchText,
    string ReplaceText,
    bool   CaseSensitive,
    bool   InNodeText        = true,
    bool   InSpeakerGuids    = false,
    bool   InScriptParams    = false,
    bool   InConditionParams = false);

public record BatchFieldMatch(
    int    NodeId,
    string FieldPath,
    string Before,
    string After);

public record BatchConversationResult(
    ConversationFile               File,
    IReadOnlyList<BatchFieldMatch> Matches);
