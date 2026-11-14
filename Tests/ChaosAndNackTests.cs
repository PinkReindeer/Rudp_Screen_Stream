using Protocol.Chaos;
using Protocol.Packets;
using Protocol.Rudp;

namespace ScreenStream.Tests;

[TestClass]
public class ChaosAndNackTests
{
    [TestMethod]
    public void ChaosSimulator_DropRate_ClampingAndPercentage()
    {
        var chaos = new ChaosSimulator();
        chaos.DropRatePercent = 10;
        Assert.AreEqual(0.10, chaos.DropRate, 0.001);
        Assert.AreEqual(10, chaos.DropRatePercent);

        chaos.DropRatePercent = 150;
        Assert.AreEqual(1.0, chaos.DropRate, 0.001);
        Assert.AreEqual(100, chaos.DropRatePercent);

        chaos.DropRatePercent = -20;
        Assert.AreEqual(0.0, chaos.DropRate, 0.001);
        Assert.AreEqual(0, chaos.DropRatePercent);
    }

    [TestMethod]
    public void ChaosSimulator_DropRate_10PercentDropsExpectedAmount()
    {
        var chaos = new ChaosSimulator { DropRatePercent = 10 };
        int dropped = 0;
        const int total = 10_000;

        for (int i = 0; i < total; i++)
        {
            if (chaos.ShouldDrop())
            {
                dropped++;
            }
        }

        Assert.AreEqual(total, chaos.TotalPacketsEvaluated);
        Assert.AreEqual(dropped, chaos.TotalPacketsDropped);

        // 10% ± 2.5% statistical tolerance for 10,000 trials
        double dropPct = chaos.ActualDropRatePercent;
        Assert.IsTrue(dropPct is >= 7.5 and <= 12.5, $"Drop rate {dropPct}% out of expected ~10% range");
    }

    [TestMethod]
    public void RingBuffer_StoresAndRetrievesAcrossWrapAround()
    {
        var ring = new RingBuffer<string>(capacity: 8); // Capacity rounded to 8

        ring.Set(0, "Item0");
        ring.Set(1, "Item1");
        ring.Set(7, "Item7");

        Assert.IsTrue(ring.TryGet(0, out var item0));
        Assert.AreEqual("Item0", item0);

        Assert.IsTrue(ring.TryGet(1, out var item1));
        Assert.AreEqual("Item1", item1);

        Assert.IsTrue(ring.TryGet(7, out var item7));
        Assert.AreEqual("Item7", item7);

        // Overwrite slot 0 with index 8
        ring.Set(8, "Item8");
        Assert.IsTrue(ring.TryGet(8, out var item8));
        Assert.AreEqual("Item8", item8);

        // Index 0 maps to same slot as 8, so getting slot 0 now returns "Item8"
        Assert.IsTrue(ring.TryGet(0, out var overwrittenItem));
        Assert.AreEqual("Item8", overwrittenItem);
    }

    [TestMethod]
    public void NackPacket_SingleNackSerializationAndParsing()
    {
        Span<byte> buffer = stackalloc byte[RudpHeader.Size];
        int written = NackPacket.WriteSingle(buffer, frameId: 42, fragmentId: 5);
        Assert.AreEqual(RudpHeader.Size, written);

        bool parsed = NackPacket.TryParse(buffer, out uint frameId, out var missing);
        Assert.IsTrue(parsed);
        Assert.AreEqual(42u, frameId);
        Assert.HasCount(1, missing);
        Assert.AreEqual((ushort)5, missing[0]);
    }

    [TestMethod]
    public void NackPacket_BatchNackSerializationAndParsing()
    {
        Span<byte> buffer = stackalloc byte[100];
        ushort[] missingInput = [2, 7, 14, 21];

        int written = NackPacket.WriteBatch(buffer, frameId: 100, missingInput);
        Assert.AreEqual(RudpHeader.Size + 8, written);

        bool parsed = NackPacket.TryParse(buffer[..written], out uint frameId, out var missing);
        Assert.IsTrue(parsed);
        Assert.AreEqual(100u, frameId);
        CollectionAssert.AreEqual(missingInput, missing);
    }

    [TestMethod]
    public void NackTracker_DetectsGapsAndTriggersNack()
    {
        var tracker = new NackTracker { IsEnabled = true };
        uint nackFrameId = 0;
        ushort[]? requestedFrags = null;

        tracker.NackRequested += (fid, frags) =>
        {
            nackFrameId = fid;
            requestedFrags = frags;
        };

        // Frame 1, Total 5 fragments
        // Receive fragment 0
        tracker.OnFragmentReceived(frameId: 1, fragmentId: 0, totalFragments: 5);
        Assert.IsNull(requestedFrags);

        // Receive fragment 3 (gap at 1 and 2!)
        tracker.OnFragmentReceived(frameId: 1, fragmentId: 3, totalFragments: 5);
        Assert.IsNotNull(requestedFrags);
        Assert.AreEqual(1u, nackFrameId);
        CollectionAssert.AreEqual(new ushort[] { 1, 2 }, requestedFrags);

        // Receiving missing fragment 1 records recovery
        tracker.OnFragmentReceived(frameId: 1, fragmentId: 1, totalFragments: 5);
        Assert.AreEqual(1, tracker.TotalRecovered);
    }

    [TestMethod]
    public void FrameReassembler_WithoutRingBuffer_DropsIncompleteFrameOnNextFrame()
    {
        using var reassembler = new FrameReassembler { EnableRingBuffer = false };

        byte[] dummyData = new byte[3500]; // 3500 bytes = 3 fragments
        ushort totalFrags = FrameFragmenter.CalculateTotalFragments(dummyData.Length);
        Assert.AreEqual((ushort)3, totalFrags);

        byte[] pktBuf = new byte[RudpHeader.MaxPacketSize];

        // Send Frame 1: total 3 fragments. Only send frag 0 and frag 2 (frag 1 dropped).
        int len0 = FrameFragmenter.WriteFragmentPacket(1, 0, totalFrags, 100, dummyData, pktBuf);
        Assert.IsFalse(reassembler.TryPushPacket(pktBuf.AsSpan(0, len0), out _));

        int len2 = FrameFragmenter.WriteFragmentPacket(1, 2, totalFrags, 100, dummyData, pktBuf);
        Assert.IsFalse(reassembler.TryPushPacket(pktBuf.AsSpan(0, len2), out _));

        // Now Frame 2 arrives: packet 0 arrives
        int lenF2P0 = FrameFragmenter.WriteFragmentPacket(2, 0, totalFrags, 200, dummyData, pktBuf);
        reassembler.TryPushPacket(pktBuf.AsSpan(0, lenF2P0), out _);

        // Frame 1 should be dropped incomplete!
        Assert.AreEqual(1, reassembler.TotalFramesDroppedIncomplete);

        // Now late retransmission of Frame 1 Fragment 1 arrives
        int lenF1P1 = FrameFragmenter.WriteFragmentPacket(1, 1, totalFrags, 100, dummyData, pktBuf);
        bool completed = reassembler.TryPushPacket(pktBuf.AsSpan(0, lenF1P1), out _);

        // Frame 1 is rejected because it moved on to Frame 2
        Assert.IsFalse(completed);
    }

    [TestMethod]
    public void FrameReassembler_WithRingBuffer_SuccessfullyRecoversMissingFragment()
    {
        using var reassembler = new FrameReassembler { EnableRingBuffer = true };

        byte[] dummyData = new byte[3500]; // 3500 bytes = 3 fragments
        ushort totalFrags = FrameFragmenter.CalculateTotalFragments(dummyData.Length);
        Assert.AreEqual((ushort)3, totalFrags);

        byte[] pktBuf = new byte[RudpHeader.MaxPacketSize];

        // Send Frame 1: total 3 fragments. Only send frag 0 and frag 2 (frag 1 missing).
        int len0 = FrameFragmenter.WriteFragmentPacket(1, 0, totalFrags, 100, dummyData, pktBuf);
        Assert.IsFalse(reassembler.TryPushPacket(pktBuf.AsSpan(0, len0), out _));

        int len2 = FrameFragmenter.WriteFragmentPacket(1, 2, totalFrags, 100, dummyData, pktBuf);
        Assert.IsFalse(reassembler.TryPushPacket(pktBuf.AsSpan(0, len2), out _));

        // Frame 2 arrives before Frame 1's retransmitted fragment
        int lenF2P0 = FrameFragmenter.WriteFragmentPacket(2, 0, totalFrags, 200, dummyData, pktBuf);
        reassembler.TryPushPacket(pktBuf.AsSpan(0, lenF2P0), out _);

        // With RingBuffer enabled, Frame 1 is NOT dropped!
        Assert.AreEqual(0, reassembler.TotalFramesDroppedIncomplete);

        // Now retransmitted Frame 1 Fragment 1 arrives!
        int lenF1P1 = FrameFragmenter.WriteFragmentPacket(1, 1, totalFrags, 100, dummyData, pktBuf);
        bool completed = reassembler.TryPushPacket(pktBuf.AsSpan(0, lenF1P1), out var completedFrame);

        // Frame 1 completes successfully!
        Assert.IsTrue(completed);
        Assert.AreEqual(1u, completedFrame.FrameID);
        completedFrame.Dispose();
        Assert.AreEqual(1, reassembler.TotalFramesCompleted);
    }

    [TestMethod]
    public void EndToEnd_SimulatedDropRate10Percent_SmoothWithRingBufferAndNack()
    {
        var chaos = new ChaosSimulator { DropRatePercent = 10 };
        using var reassembler = new FrameReassembler { EnableRingBuffer = true };
        var nackTracker = new NackTracker { IsEnabled = true };

        byte[] frameData = new byte[10_000]; // ~8 fragments per frame
        ushort totalFrags = FrameFragmenter.CalculateTotalFragments(frameData.Length);
        byte[] pktBuf = new byte[RudpHeader.MaxPacketSize];

        // Sender cache simulating sender's RingBuffer
        var senderCache = new Dictionary<(uint FrameId, ushort FragId), byte[]>();

        // Wire up NACK requests to sender retransmissions
        nackTracker.NackRequested += (frameId, missingFrags) =>
        {
            foreach (var fragId in missingFrags)
            {
                if (senderCache.TryGetValue((frameId, fragId), out var cachedPacket))
                {
                    // Retransmission passes through receiver (retransmission also subject to chaos, but retry overcomes it)
                    // If chaos drops it, CheckTimeouts retry will resend
                    if (!chaos.Intercept(cachedPacket))
                    {
                        nackTracker.OnFragmentReceived(frameId, fragId, totalFrags);
                        if (reassembler.TryPushPacket(cachedPacket, out var frame))
                        {
                            nackTracker.OnFrameCompleted(frame.FrameID);
                            frame.Dispose();
                        }
                    }
                }
            }
        };

        const int numFrames = 20;

        for (uint frameId = 1; frameId <= numFrames; frameId++)
        {
            for (ushort fragId = 0; fragId < totalFrags; fragId++)
            {
                int len = FrameFragmenter.WriteFragmentPacket(frameId, fragId, totalFrags, 1000, frameData, pktBuf);
                byte[] packetCopy = pktBuf[..len];
                senderCache[(frameId, fragId)] = packetCopy;

                // Pass through ChaosSimulator (10% drop)
                if (chaos.Intercept(packetCopy))
                {
                    continue; // dropped!
                }

                nackTracker.OnFragmentReceived(frameId, fragId, totalFrags);
                if (reassembler.TryPushPacket(packetCopy, out var frame))
                {
                    nackTracker.OnFrameCompleted(frame.FrameID);
                    frame.Dispose();
                }
            }

            // Simulate small network tick for timeout checking
            for (int tick = 0; tick < 5; tick++)
            {
                Thread.Sleep(2);
                nackTracker.CheckTimeouts();
            }
        }

        // Allow final retries to settle
        for (int tick = 0; tick < 10; tick++)
        {
            Thread.Sleep(5);
            nackTracker.CheckTimeouts();
        }

        // Verify that packets were indeed dropped by chaos
#pragma warning disable MSTEST0037
        Assert.IsTrue(chaos.TotalPacketsDropped > 0, "Chaos simulator should have dropped some packets at 10% rate");
#pragma warning restore MSTEST0037

        // Verify that with RingBuffer + NACK, all frames completed!
        Assert.AreEqual(numFrames, reassembler.TotalFramesCompleted,
            $"Expected all {numFrames} frames to complete via NACK retransmission, but got {reassembler.TotalFramesCompleted}");
    }

    [TestMethod]
    public void Comparison_RingBufferAndNackOffVsOn_DemonstratesLossThenRecovery()
    {
        byte[] frameData = new byte[15_000]; // ~11 fragments per frame
        ushort totalFrags = FrameFragmenter.CalculateTotalFragments(frameData.Length);
        byte[] pktBuf = new byte[RudpHeader.MaxPacketSize];

        // PHASE 1: RingBuffer = OFF, NACK = OFF, Drop Rate = 10%
        var chaos1 = new ChaosSimulator { DropRatePercent = 10 };
        using var reassemblerOff = new FrameReassembler { EnableRingBuffer = false };

        const int testFrames = 25;
        for (uint frameId = 1; frameId <= testFrames; frameId++)
        {
            for (ushort fragId = 0; fragId < totalFrags; fragId++)
            {
                int len = FrameFragmenter.WriteFragmentPacket(frameId, fragId, totalFrags, 1000, frameData, pktBuf);
                if (chaos1.Intercept(pktBuf.AsSpan(0, len)))
                {
                    continue; // dropped
                }
                reassemblerOff.TryPushPacket(pktBuf.AsSpan(0, len), out _);
            }
        }

        // Without RingBuffer & NACK, incomplete frames are dropped
#pragma warning disable MSTEST0037
        Assert.IsTrue(reassemblerOff.TotalFramesDroppedIncomplete > 0, 
            "Without RingBuffer and NACK, frames should be dropped when packets are lost");
        Assert.IsTrue(reassemblerOff.TotalFramesCompleted < testFrames,
            "Completed frames should be fewer than total frames due to packet loss");
#pragma warning restore MSTEST0037

        // PHASE 2: RingBuffer = ON, NACK = ON, Drop Rate = 10% (Same drop rate!)
        var chaos2 = new ChaosSimulator { DropRatePercent = 10 };
        using var reassemblerOn = new FrameReassembler { EnableRingBuffer = true };
        var nackOn = new NackTracker { IsEnabled = true };

        var senderCache = new Dictionary<(uint, ushort), byte[]>();

        nackOn.NackRequested += (frameId, missingFrags) =>
        {
            foreach (var fragId in missingFrags)
            {
                if (senderCache.TryGetValue((frameId, fragId), out var cached))
                {
                    // Retransmission passes through receiver (resilient to loss via retries)
                    if (!chaos2.Intercept(cached))
                    {
                        nackOn.OnFragmentReceived(frameId, fragId, totalFrags);
                        if (reassemblerOn.TryPushPacket(cached, out var cf))
                        {
                            nackOn.OnFrameCompleted(cf.FrameID);
                            cf.Dispose();
                        }
                    }
                }
            }
        };

        for (uint frameId = 1; frameId <= testFrames; frameId++)
        {
            for (ushort fragId = 0; fragId < totalFrags; fragId++)
            {
                int len = FrameFragmenter.WriteFragmentPacket(frameId, fragId, totalFrags, 1000, frameData, pktBuf);
                byte[] copy = pktBuf[..len];
                senderCache[(frameId, fragId)] = copy;

                if (chaos2.Intercept(copy))
                {
                    continue; // dropped
                }

                nackOn.OnFragmentReceived(frameId, fragId, totalFrags);
                if (reassemblerOn.TryPushPacket(copy, out var cf))
                {
                    nackOn.OnFrameCompleted(cf.FrameID);
                    cf.Dispose();
                }
            }

            for (int tick = 0; tick < 6; tick++)
            {
                Thread.Sleep(2);
                nackOn.CheckTimeouts();
            }
        }

        for (int tick = 0; tick < 10; tick++)
        {
            Thread.Sleep(5);
            nackOn.CheckTimeouts();
        }

        // With RingBuffer & NACK enabled, all 25 frames are completed!
        Assert.AreEqual(testFrames, reassemblerOn.TotalFramesCompleted,
            "With RingBuffer and NACK enabled, all frames should complete smoothly despite 10% drop rate");
        Assert.AreEqual(0, reassemblerOn.TotalFramesDroppedIncomplete,
            "No frames should be dropped incomplete when RingBuffer and NACK are enabled");
    }
}
