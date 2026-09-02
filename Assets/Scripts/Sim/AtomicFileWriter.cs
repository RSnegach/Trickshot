using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Writes a small text file atomically (temp file, then swap over the real one) OFF the main
    /// thread. The lifetime stats and achievements save on every recorded event - a cross, a save,
    /// a goal, a kick - and a synchronous WriteAllText + File.Replace in the middle of play was a
    /// disk hitch each time. Here the caller hands over the finished text and returns at once.
    ///
    /// One worker per path at a time, and the LATEST text wins: a burst of saves (a goal records
    /// two stats and an achievement check) collapses into one write, and two writers can never
    /// race on the same file. FlushAll() blocks until everything pending has landed; GameBootstrap
    /// calls it at quit so a save fired on the last frame is not lost to process exit.
    /// </summary>
    public static class AtomicFileWriter
    {
        class Job { public string path; public string text; public string tag; public bool running; }

        static readonly Dictionary<string, Job> s_jobs = new Dictionary<string, Job>();
        static readonly object s_lock = new object();
        static int s_inFlight;

        /// <summary>Queue `text` for `path`. Returns immediately. `tag` names the caller in a warning.</summary>
        public static void Write(string path, string text, string tag = null)
        {
            if (string.IsNullOrEmpty(path) || text == null) return;
            lock (s_lock)
            {
                if (!s_jobs.TryGetValue(path, out var job))
                {
                    job = new Job { path = path, tag = tag ?? "AtomicFileWriter" };
                    s_jobs[path] = job;
                }
                job.text = text;                // newest wins; a running worker picks it up
                if (job.running) return;
                job.running = true;
                s_inFlight++;
                ThreadPool.QueueUserWorkItem(_ => Run(job));
            }
        }

        static void Run(Job job)
        {
            while (true)
            {
                string text;
                lock (s_lock)
                {
                    text = job.text;
                    job.text = null;
                    if (text == null)
                    {
                        // Nothing newer arrived while writing: done. Decided under the lock so a
                        // Write() racing this cannot slip between the check and the flag.
                        job.running = false;
                        s_inFlight--;
                        Monitor.PulseAll(s_lock);
                        return;
                    }
                }
                WriteNow(job.path, text, job.tag);
            }
        }

        static void WriteNow(string path, string text, string tag)
        {
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, text);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning(tag + ": failed to save. " + e.Message); }   // Debug.Log is thread-safe
        }

        /// <summary>Block until every queued write has landed (bounded by `timeoutMs`).</summary>
        public static void FlushAll(int timeoutMs = 2000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            lock (s_lock)
            {
                while (s_inFlight > 0)
                {
                    int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0) break;
                    Monitor.Wait(s_lock, remaining);
                }
            }
        }
    }
}
