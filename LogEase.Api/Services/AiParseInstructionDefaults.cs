namespace LogEase.Api.Services;

/// <summary>
/// Default wording that teaches the LLM how informal team notes map into LogEase rows.
/// Users can replace/extend this via Customize AI wording (saved per account).
/// Named differently from the GraphQL field to avoid HotChocolate name clashes.
/// </summary>
public static class AiParseInstructionDefaults
{
    public const string Text =
        """
        How our team writes free text (interpret flexibly — typos and shorthand are normal):

        - Project names may be informal or abbreviated (e.g. "company announcement", "perception survey", "CA", "PS"). Match the closest catalog/Kimai project when possible.
        - Phrases like "other project is X", "also worked on Y", "mainly on Z" mean multiple projects — create rows for each.
        - Work wording may be messy ("debugging issues to find duplicate data on translate table") — turn that into a short professional description without inventing extra tasks.
        - If they omit hours, assume a normal office day (~7.5–9h) split across the projects they mentioned.
        - If they omit dates, you MUST use only the app From/To date range. Never invent other days.
        - Activities: prefer catalog names (Development, Bug Fix, Meeting, etc.) from what they described.

        Goal: whatever format the employee pastes, produce clear LogEase table rows (date, project, activity, hours, description).
        """;
}
