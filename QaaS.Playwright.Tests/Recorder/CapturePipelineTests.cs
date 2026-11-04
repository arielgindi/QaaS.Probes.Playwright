using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The capture pipeline between the pages and the recording: what began while recording is recorded exactly once
/// however late it comes, nothing else is, and a drain sees everything a page sent before it answered. Simulated pages
/// send with random delays and out of order, hold typing back, learn each new epoch late, and some never answer.
/// </summary>
[TestFixture]
public class CapturePipelineTests
{
    [Test]
    public async Task WhatBeganWhileRecording_IsRecordedOnce_HoweverLate_AndADrainSeesAllSentBeforeIt([Range(1, 40)] int seed)
    {
        var random = new Random(seed);
        var pipeline = new CapturePipeline(recording: random.Next(2) == 0);
        var pages = Enumerable.Range(0, 1 + random.Next(3)).Select(index => new Page($"doc{index}", pipeline.Epoch)).ToList();
        var recorded = new List<Message>();
        var deliveries = new List<Task>();
        var unanswered = pages[0];

        // A message comes after a few milliseconds, in any order.
        void Send(Message message) => deliveries.Add(Task.Run(async () =>
        {
            await Task.Delay(random.Next(4));
            if (pipeline.Take(message.Document, message.Epoch, message.Sequence) == Verdict.Record)
                lock (recorded) recorded.Add(message);
        }));

        for (var move = 0; move < 60; move++)
        {
            var page = pages[random.Next(pages.Count)];
            switch (random.Next(5))
            {
                case 0 or 1:
                    // An action, sent at once, or held back as a field being typed is.
                    if (page.Act(holdBack: random.Next(2) == 0) is { } sent) Send(sent);
                    break;
                case 2:
                    // A held-back field the person stopped typing in.
                    foreach (var held in page.Flush()) Send(held);
                    break;
                default:
                    // A transition: drain every page, then commit; each page learns the new epoch a moment later.
                    var sentBefore = pages.ToDictionary(each => each.Id, each => each.Sequence);
                    var late = random.Next(4) == 0 ? unanswered : null;
                    var drained = await pipeline.DrainAsync(_ =>
                    {
                        var answers = new Dictionary<string, long?>();
                        foreach (var each in pages)
                        {
                            if (each == late) { answers[each.Id] = null; continue; }
                            foreach (var held in each.Flush()) Send(held);
                            answers[each.Id] = each.Sequence;
                        }

                        return Task.FromResult<IReadOnlyDictionary<string, long?>>(answers);
                    }, TimeSpan.FromSeconds(2));
                    foreach (var each in pages.Where(each => each != late))
                        Assert.That(pipeline.ReceivedFrom(each.Id), Is.GreaterThanOrEqualTo(sentBefore[each.Id]),
                            $"seed {seed}: the drain returned before {each.Id}'s messages came");
                    Assert.That(drained.Late, late is null ? Is.Empty : Is.EqualTo(new[] { late.Id }));
                    var epoch = pipeline.Commit(random.Next(2) == 0);
                    foreach (var each in pages)
                        deliveries.Add(Task.Run(async () => { await Task.Delay(random.Next(3)); each.Learn(epoch); }));
                    break;
            }
        }

        foreach (var page in pages)
        foreach (var held in page.Flush())
            Send(held);
        await Task.WhenAll(deliveries);

        var expected = pages.SelectMany(page => page.Sent).Where(message => pipeline.Records(message.Epoch)).ToList();
        Assert.That(recorded.OrderBy(message => message.Document).ThenBy(message => message.Sequence),
            Is.EqualTo(expected.OrderBy(message => message.Document).ThenBy(message => message.Sequence)), $"seed {seed}");
    }

    [Test]
    public void Epochs_GoForward_AndAMessageWithoutOne_IsLeftToTheLog()
    {
        var pipeline = new CapturePipeline(recording: false);
        var resumed = pipeline.Commit(5, recording: true);

        Assert.Multiple(() =>
        {
            Assert.That(() => pipeline.Commit(5, recording: false), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((pipeline.Records(0), pipeline.Records(resumed), pipeline.Records(9)), Is.EqualTo((false, true, false)));
            Assert.That(pipeline.Take("doc", epoch: null, sequence: null), Is.EqualTo(Verdict.Unstamped));
            Assert.That(pipeline.Take("doc", resumed, 2), Is.EqualTo(Verdict.Record));
            Assert.That(pipeline.ReceivedFrom("doc"), Is.Zero, "message 1 has not come yet");
            Assert.That(pipeline.Take("doc", 0, 1), Is.EqualTo(Verdict.Drop));
            Assert.That(pipeline.ReceivedFrom("doc"), Is.EqualTo(2));
        });
    }

    private sealed record Message(string Document, long Sequence, long Epoch);

    // A page as the capture script is: each action stamped with the epoch the page knew when it began, held back or not.
    private sealed class Page(string id, long epoch)
    {
        private readonly Lock _gate = new();
        private readonly List<long> _held = [];
        private long _epoch = epoch;

        public string Id { get; } = id;
        public long Sequence { get; private set; }
        public List<Message> Sent { get; } = [];

        public Message? Act(bool holdBack)
        {
            lock (_gate)
            {
                if (!holdBack) return Next(_epoch);
                _held.Add(_epoch);
                return null;
            }
        }

        public List<Message> Flush()
        {
            lock (_gate)
            {
                var flushed = _held.Select(Next).ToList();
                _held.Clear();
                return flushed;
            }
        }

        public void Learn(long epoch)
        {
            lock (_gate) _epoch = Math.Max(_epoch, epoch);
        }

        private Message Next(long began)
        {
            var message = new Message(Id, ++Sequence, began);
            Sent.Add(message);
            return message;
        }
    }
}
