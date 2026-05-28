using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Records the outcome of every match — personality + CharacterStats of winner and
/// loser — to a timestamped CSV for post-hoc permutation analysis.
///
/// One instance per process (singleton). ArenaManager auto-bootstraps it, so no
/// scene-builder change is needed.
///
/// PERFORMANCE: rows are buffered in memory (AutoFlush off). The only per-match cost
/// is a formatted string appended to the writer's buffer — no syscall, no disk I/O.
/// The buffer is flushed to disk on a timer (flushInterval) and on quit. Worst-case
/// data loss on a hard crash is flushInterval seconds of matches — fine for a log.
///
/// Analyze with: python tools/report.py
/// </summary>
public class MatchLogger : MonoBehaviour
{
    public static MatchLogger Instance { get; private set; }

    [Tooltip("Seconds between disk flushes. Per-match cost is just an in-memory " +
             "string append; actual I/O happens only on this timer.")]
    public float flushInterval = 15f;

    private StreamWriter writer;
    private string filePath;
    private int rowsSinceFlush;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Resolve match_logs/ against the working directory — project root in the
        // Editor, the directory mlagents-learn was launched from for standalone.
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "match_logs");
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filePath = Path.Combine(dir, $"matches_{stamp}.csv");

        // AutoFlush off — WriteLine only touches an in-memory buffer.
        writer = new StreamWriter(filePath, append: false) { AutoFlush = false };
        writer.WriteLine("time,winner_personality,winner_stats,loser_personality,loser_stats,duration_s");
        writer.Flush();

        InvokeRepeating(nameof(FlushBuffer), flushInterval, flushInterval);
        Debug.Log($"[MatchLogger] Logging match outcomes to {filePath}");
    }

    /// <summary>Record one decided match. Called once per loser by ArenaManager.</summary>
    public void LogMatch(Player winner, Player loser, float durationSeconds)
    {
        if (writer == null) return;
        writer.WriteLine(
            $"{Time.time:F1},{Personality(winner)},{Stats(winner)}," +
            $"{Personality(loser)},{Stats(loser)},{durationSeconds:F1}");
        rowsSinceFlush++;
    }

    private void FlushBuffer()
    {
        if (writer != null && rowsSinceFlush > 0)
        {
            writer.Flush();
            rowsSinceFlush = 0;
        }
    }

    private static string Personality(Player p)
    {
        var rl = p != null ? p.Brain as RLAgentBrain : null;
        return rl != null && rl.personality != null ? rl.personality.name : "none";
    }

    private static string Stats(Player p) =>
        p != null && p.stats != null ? p.stats.name : "none";

    private void Close()
    {
        if (writer == null) return;
        writer.Flush();
        writer.Close();
        writer = null;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Close();
    }

    void OnApplicationQuit() => Close();
}
