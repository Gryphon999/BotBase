using BotBase.Api.Data.Entities;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace BotBase.Api.Services;

// Выбирает из базы знаний только куски, относящиеся к вопросу клиента, вместо отправки всей базы в ИИ.
// Простой поиск по словам с «обрезанием окончаний» (русские формы слов: парковка/парковки/парковке).
public partial class KnowledgeRetriever(IOptions<BotOptions> options)
{
    const int PassageChars = 900;

    public string Select(IEnumerable<KnowledgeChunk> chunks, string query)
    {
        var all = chunks.OrderBy(c => c.UploadedAt).ToList();
        var budget = options.Value.KnowledgeBudgetChars;
        var fullText = string.Join("\n\n", all.Select(c => c.ExtractedText));
        if (fullText.Length <= budget) return fullText; // маленькая база — отдаём целиком

        // Distinct: клиенты часто загружают файлы, где следующий содержит предыдущий — не отправляем одно и то же трижды.
        var passages = all.SelectMany(c => Split(c.ExtractedText)).Distinct()
            .Where(p => p.Count(ch => ch == (char)0xFFFD) < p.Length * 0.3) // куски из «����» (сломанная кодировка) — мусор для ИИ
            .ToList();
        if (passages.Count == 0) return "";
        var queryStems = Stems(query).ToHashSet();

        // Начало базы (обычно «о нас», адрес, контакты) берём всегда, остальное — по релевантности.
        var picked = new SortedSet<int> { 0 };
        if (queryStems.Count > 0)
        {
            var df = new Dictionary<string, int>();
            var passageStems = passages.Select(p => Stems(p).ToHashSet()).ToList();
            foreach (var set in passageStems) foreach (var s in set) df[s] = df.GetValueOrDefault(s) + 1;

            var ranked = passageStems
                .Select((set, i) => (i, score: set.Where(queryStems.Contains).Sum(s => Math.Log(1 + (double)passages.Count / df[s]))))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score);
            foreach (var (i, _) in ranked)
            {
                if (picked.Sum(p => passages[p].Length) + passages[i].Length > budget) break;
                picked.Add(i);
            }
        }
        return string.Join("\n…\n", picked.Select(i => passages[i]));
    }

    // Режем по абзацам, склеивая короткие до ~PassageChars.
    static IEnumerable<string> Split(string text)
    {
        var buf = new System.Text.StringBuilder();
        foreach (var para in text.Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (buf.Length > 0 && buf.Length + para.Length > PassageChars)
            {
                yield return buf.ToString();
                buf.Clear();
            }
            if (para.Length > PassageChars * 2)
            {
                for (var i = 0; i < para.Length; i += PassageChars) yield return para.Substring(i, Math.Min(PassageChars, para.Length - i));
                continue;
            }
            if (buf.Length > 0) buf.Append('\n');
            buf.Append(para);
        }
        if (buf.Length > 0) yield return buf.ToString();
    }

    static IEnumerable<string> Stems(string text) =>
        WordRegex().Matches(text.ToLowerInvariant().Replace('ё', 'е'))
            .Select(m => m.Value)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(w => w.Length > 5 ? w[..5] : w);

    static readonly HashSet<string> StopWords = ["это", "как", "что", "для", "или", "так", "вас", "нас", "мне", "можно", "есть", "хочу", "здравствуйте", "добрый", "день", "спасибо", "пожалуйста"];

    [GeneratedRegex(@"[\p{L}\d]+")]
    private static partial Regex WordRegex();
}
