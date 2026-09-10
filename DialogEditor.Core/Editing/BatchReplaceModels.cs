using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

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

/// Which of a node's replaceable fields a batch-replace match landed in.
public enum BatchFieldKind
{
    DefaultText,
    FemaleText,
    SpeakerGuid,
    ListenerGuid,
    ScriptParam,      // Index = script position on the node, ParamIndex = argument
    ConditionParam,   // Index = leaf position in flattened order, ParamIndex = argument
}

/// The identity of one replaceable field within a node.
///
/// This is deliberately typed data and not the string the preview shows. Apply
/// re-loads each conversation and pairs its fresh snapshot back to the DryRun matches
/// by value equality on this record, so the identity must not depend on the UI
/// language: it used to BE the display label ("Default Text"), which meant localising
/// that label — as the editor now does — would have made a language change between
/// preview and apply turn Apply into a silent no-op. The label is built from this by
/// BatchReplaceMatchViewModel.FieldLabel.
///
/// ScriptCategory is meaningful only for ScriptParam; Index/ParamIndex only for the
/// two indexed kinds. They default so the four plain kinds construct as
/// new BatchField(BatchFieldKind.DefaultText).
public record BatchField(
    BatchFieldKind Kind,
    ScriptCategory ScriptCategory = default,
    int            Index          = 0,
    int            ParamIndex     = 0);

public record BatchFieldMatch(
    int        NodeId,
    BatchField Field,
    string     Before,
    string     After);

public record BatchConversationResult(
    ConversationFile               File,
    IReadOnlyList<BatchFieldMatch> Matches);
