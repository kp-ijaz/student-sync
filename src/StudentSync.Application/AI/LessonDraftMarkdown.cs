using System.Text;
using StudentSync.Domain.AI;

namespace StudentSync.Application.AI;

/// <summary>Renders an approved draft as student-facing Markdown, clearly separating AI recommendations from what was taught.</summary>
public static class LessonDraftMarkdown
{
    public static string Render(LessonDraftContent c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("> Prepared with AI assistance from the class transcript and reviewed and approved by your teacher.").AppendLine();
        sb.AppendLine("## Recap").AppendLine(c.Recap).AppendLine();
        List(sb, "Learning outcomes", c.LearningOutcomes);
        if (c.TopicNotes.Count > 0)
        {
            sb.AppendLine("## Topic notes");
            foreach (var t in c.TopicNotes) sb.AppendLine($"### {t.Topic}").AppendLine(t.Notes).AppendLine();
        }
        if (c.Definitions.Count > 0)
        {
            sb.AppendLine("## Definitions and formulas");
            foreach (var d in c.Definitions)
                sb.AppendLine($"- **{d.Term}** — {d.Definition}{(string.IsNullOrWhiteSpace(d.Formula) ? "" : $"  \n  `{d.Formula}`")}");
            sb.AppendLine();
        }
        if (c.Examples.Count > 0)
        {
            sb.AppendLine("## Worked examples");
            foreach (var e in c.Examples)
            {
                sb.AppendLine($"### {e.Title}");
                for (var i = 0; i < e.Steps.Count; i++) sb.AppendLine($"{i + 1}. {e.Steps[i]}");
                sb.AppendLine();
            }
        }
        if (c.QuestionsAnswered.Count > 0)
        {
            sb.AppendLine("## Questions discussed in class");
            foreach (var q in c.QuestionsAnswered) sb.AppendLine($"- **Q:** {q.Question}  \n  **A:** {q.Answer}");
            sb.AppendLine();
        }
        List(sb, "Questions still open", c.UnansweredQuestions);
        if (c.Homework.Count > 0)
        {
            sb.AppendLine("## Homework mentioned in class");
            foreach (var h in c.Homework) sb.AppendLine($"- {h.Description}{(h.StatedDeadline is null ? "" : $" (stated deadline: {h.StatedDeadline})")}");
            sb.AppendLine();
        }
        sb.AppendLine("## Revision summary").AppendLine(c.RevisionSummary).AppendLine();

        if (c.PracticeQuestions.Count > 0 || c.Flashcards.Count > 0 || c.NextClassAgenda.Count > 0)
        {
            sb.AppendLine("---").AppendLine("## AI-suggested practice (not teacher instructions)");
            foreach (var p in c.PracticeQuestions)
            {
                sb.AppendLine($"- {p.Question}");
                if (p.Options is { Count: > 0 }) foreach (var o in p.Options) sb.AppendLine($"  - {o}");
                sb.AppendLine($"  <details><summary>Answer</summary>{p.Answer} — {p.Explanation}</details>");
            }
            if (c.Flashcards.Count > 0)
            {
                sb.AppendLine().AppendLine("### Flashcards");
                foreach (var f in c.Flashcards) sb.AppendLine($"- **{f.Front}** — {f.Back}");
            }
        }
        return sb.ToString();
    }

    private static void List(StringBuilder sb, string title, List<string> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"## {title}");
        foreach (var i in items) sb.AppendLine($"- {i}");
        sb.AppendLine();
    }
}
