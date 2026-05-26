using DialogEditor.Core.GameData;

namespace DialogEditor.Core.Editing;

public record BatchReplaceQuery(
    string SearchText,
    string ReplaceText,
    bool   CaseSensitive,
    bool   InNodeText        = true,
    bool   InSpeakerGuids    = false,
    bool   InScriptParams    = false,
    bool   InConditionParams = false,
    bool   InLinkChoiceText  = false);

public record BatchFieldMatch(
    int    NodeId,
    string FieldPath,
    string Before,
    string After);

public record BatchConversationResult(
    ConversationFile               File,
    IReadOnlyList<BatchFieldMatch> Matches);
