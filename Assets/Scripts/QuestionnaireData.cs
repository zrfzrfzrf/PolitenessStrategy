public static class QuestionnaireData
{
    public readonly struct Question
    {
        public readonly string Id, Text;
        public readonly string[] Labels;

        public Question(string id, string text, string[] labels)
        {
            Id = id;
            Text = text;
            Labels = labels;
        }
    }

    // Perception[0..Part1Count) are asked by the first switch, the rest by the second.
    public const int Part1Count = 4;

    static readonly string[] Agree = { "Totally disagree", "", "", "", "", "", "", "", "Totally agree" };

    public static readonly Question[] Tlx =
    {
        new Question("Mental", "Mental strain: During the previous activity, I invested",
            Bipolar("low mental effort", "high mental effort", "Neither low nor high mental effort")),
        new Question("Physical", "Physical strain: During the previous activity, I invested",
            Bipolar("low physical exertion", "high physical exertion", "Neither low nor high physical exertion")),
        new Question("TimePressure", "Time pressure: During the previous activity, I experienced",
            Bipolar("low time pressure", "high time pressure", "Neither low nor high time pressure")),
        new Question("Performance", "Performance: During the previous activity, I performed",
            Bipolar("good", "bad", "Neither good nor bad")),
        new Question("Effort", "Effort: To perform as I did, I invested",
            Bipolar("low effort", "high effort", "Neither low nor high effort")),
        new Question("Frustration", "Frustration: During the previous activity, I experienced",
            Bipolar("little frustration", "much frustration", "Neither little nor much frustration")),
    };

    public static readonly Question[] Perception =
    {
        new Question("Understand", "I could understand exactly what the agent wanted.", Agree),
        new Question("Offended", "I was offended by the agent's actions.", Agree),
        new Question("Intimacy", "The agent wanted to increase intimacy with me", Agree),
        new Question("Freedom", "The agent respected my freedom of action", Agree),
        new Question("Courtesy", "Courtesy: During the previous activity, I experienced the agent as",
            Bipolar("polite", "rude", "Neither polite nor rude")),
        new Question("Trust", "Trust: During the previous activity, I experienced the agent as",
            Bipolar("reliable", "unreliable", "Neither reliable nor unreliable")),
        new Question("SelfConfidence", "Self-confidence: During the previous activity I",
            Bipolar("confident", "uncertain", "Neither confident nor uncertain")),
    };

    static string[] Bipolar(string positive, string negative, string middle) => new[]
    {
        "Very, very " + positive, "Very " + positive, Cap(positive), "Fairly " + positive, middle,
        "Fairly " + negative, Cap(negative), "Very " + negative, "Very, very " + negative
    };

    static string Cap(string s) => char.ToUpper(s[0]) + s.Substring(1);
}
