﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.EmulatorTests.FeedRanges
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Query.Core.ContinuationTokens;
    using Microsoft.Azure.Cosmos.SDK.EmulatorTests;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [SDK.EmulatorTests.TestClass]
    public class FeedRangeTests : BaseCosmosClientHelper
    {
        private ContainerInternal Container = null;

        [TestInitialize]
        public async Task TestInitialize()
        {
            await base.TestInit();
            string PartitionKey = "/status";
            ContainerResponse largerContainer = await this.database.CreateContainerAsync(
                new ContainerProperties(id: Guid.NewGuid().ToString(), partitionKeyPath: PartitionKey),
                throughput: 20000,
                cancellationToken: this.cancellationToken);

            this.Container = (ContainerInlineCore)largerContainer;
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            await base.TestCleanup();
        }

        [TestMethod]
        public async Task FeedRange_EPK_Serialization()
        {
            string continuation = "TBD";
            string containerRid = Guid.NewGuid().ToString();
            List<FeedRange> ranges = (await this.Container.GetFeedRangesAsync()).ToList();
            List<string> serializations = new List<string>();
            List<FeedRangeCompositeContinuation> tokens = new List<FeedRangeCompositeContinuation>();
            foreach (FeedRange range in ranges)
            {
                FeedRangeEPK feedRangeEPK = range as FeedRangeEPK;
                FeedRangeCompositeContinuation feedRangeCompositeContinuation = new FeedRangeCompositeContinuation(containerRid, feedRangeEPK, new List<Documents.Routing.Range<string>>() { feedRangeEPK.Range }, continuation);
                tokens.Add(feedRangeCompositeContinuation);
                serializations.Add(feedRangeCompositeContinuation.ToString());
            }

            List<FeedRangeContinuation> deserialized = new List<FeedRangeContinuation>();
            foreach(string serialized in serializations)
            {
                Assert.IsTrue(FeedRangeContinuation.TryParse(serialized, out FeedRangeContinuation token));
                deserialized.Add(token);
            }

            Assert.AreEqual(tokens.Count, deserialized.Count);

            for (int i = 0; i < tokens.Count; i++)
            {
                FeedRangeCompositeContinuation originalToken = tokens[i] as FeedRangeCompositeContinuation;
                FeedRangeCompositeContinuation deserializedToken = deserialized[i] as FeedRangeCompositeContinuation;
                Assert.AreEqual(originalToken.GetContinuation(), deserializedToken.GetContinuation());
                Assert.AreEqual(originalToken.ContainerRid, deserializedToken.ContainerRid);
                Assert.AreEqual(originalToken.CompositeContinuationTokens.Count, deserializedToken.CompositeContinuationTokens.Count);
                Assert.AreEqual(originalToken.CompositeContinuationTokens.Peek().Token, deserializedToken.CompositeContinuationTokens.Peek().Token);
                Assert.AreEqual(originalToken.CompositeContinuationTokens.Peek().Range.Min, deserializedToken.CompositeContinuationTokens.Peek().Range.Min);
                Assert.AreEqual(originalToken.CompositeContinuationTokens.Peek().Range.Max, deserializedToken.CompositeContinuationTokens.Peek().Range.Max);
                Assert.AreEqual(originalToken.CompositeContinuationTokens.Peek().Range.IsMinInclusive, deserializedToken.CompositeContinuationTokens.Peek().Range.IsMinInclusive);
                Assert.AreEqual(originalToken.CompositeContinuationTokens.Peek().Range.IsMaxInclusive, deserializedToken.CompositeContinuationTokens.Peek().Range.IsMaxInclusive);
            }
        }

        [TestMethod]
        public void FeedRange_PartitionKey_Serialization()
        {
            this.FeedRange_PartitionKey_Validate(new PartitionKey("TBD"));
            this.FeedRange_PartitionKey_Validate(new PartitionKey(10));
            this.FeedRange_PartitionKey_Validate(new PartitionKey(15.6));
            this.FeedRange_PartitionKey_Validate(new PartitionKey(true));
            this.FeedRange_PartitionKey_Validate(PartitionKey.Null);
        }

        [TestMethod]
        public async Task FeedRange_PKRangeId_Serialization()
        {
            string continuationToken = "TBD";
            string containerRid = Guid.NewGuid().ToString();
            DocumentFeedResponse<Documents.PartitionKeyRange> ranges = await this.Container.ClientContext.DocumentClient.ReadPartitionKeyRangeFeedAsync(this.Container.LinkUri);
            Documents.PartitionKeyRange oneRange = ranges.First();

            FeedRangePartitionKeyRange original = new FeedRangePartitionKeyRange(oneRange.Id);
            FeedRangeCompositeContinuation feedRangeSimpleContinuation = new FeedRangeCompositeContinuation(containerRid, original, new List<Documents.Routing.Range<string>>() { oneRange.ToRange() }, continuationToken);
            string serialized = feedRangeSimpleContinuation.ToString();
            Assert.IsTrue(FeedRangeContinuation.TryParse(serialized, out FeedRangeContinuation feedRangeContinuation));
            FeedRangeCompositeContinuation deserialized = feedRangeContinuation as FeedRangeCompositeContinuation;
            FeedRangePartitionKeyRange deserializedFeedRange = deserialized.FeedRange as FeedRangePartitionKeyRange;
            Assert.IsNotNull(deserialized, "Error deserializing to FeedRangePartitionKeyRange");
            Assert.AreEqual(original.PartitionKeyRangeId, deserializedFeedRange.PartitionKeyRangeId);
            Assert.AreEqual(continuationToken, deserialized.GetContinuation());
        }

        [TestMethod]
        public async Task GetPartitionKeyRangesAsync_WithEPKToken()
        {
            DocumentFeedResponse<Documents.PartitionKeyRange> ranges = await this.Container.ClientContext.DocumentClient.ReadPartitionKeyRangeFeedAsync(this.Container.LinkUri);
            int pkRangesCount = ranges.Count;
            List<FeedRange> tokens = (await this.Container.GetFeedRangesAsync()).ToList();
            List<string> resolvedRanges = new List<string>();
            foreach(FeedRange token in tokens)
            {
                resolvedRanges.AddRange(await this.Container.GetPartitionKeyRangesAsync(token));
            }

            Assert.AreEqual(pkRangesCount, resolvedRanges.Count);
            foreach (Documents.PartitionKeyRange range in ranges)
            {
                Assert.IsTrue(resolvedRanges.Contains(range.Id));
            }

            foreach (string id in resolvedRanges)
            {
                Assert.IsTrue(ranges.Any(range => range.Id == id));
            }
        }

        [TestMethod]
        public async Task GetPartitionKeyRangesAsync_WithPKToken()
        {
            DocumentFeedResponse<Documents.PartitionKeyRange> ranges = await this.Container.ClientContext.DocumentClient.ReadPartitionKeyRangeFeedAsync(this.Container.LinkUri);

            FeedRange feedToken = new FeedRangePartitionKey(new PartitionKey("TBD"));
            List<string> resolvedRanges = (await this.Container.GetPartitionKeyRangesAsync(feedToken)).ToList();

            Assert.AreEqual(1, resolvedRanges.Count, "PK value should resolve to a single range");

            foreach (string id in resolvedRanges)
            {
                Assert.IsTrue(ranges.Any(range => range.Id == id));
            }
        }

        [TestMethod]
        public async Task GetPartitionKeyRangesAsync_WithPKRangeIdToken()
        {
            DocumentFeedResponse<Documents.PartitionKeyRange> ranges = await this.Container.ClientContext.DocumentClient.ReadPartitionKeyRangeFeedAsync(this.Container.LinkUri);

            FeedRange feedToken = new FeedRangePartitionKeyRange(ranges.First().Id);
            List<string> resolvedRanges = (await this.Container.GetPartitionKeyRangesAsync(feedToken)).ToList();

            Assert.AreEqual(1, resolvedRanges.Count);

            foreach (string id in resolvedRanges)
            {
                Assert.IsTrue(ranges.Any(range => range.Id == id));
            }
        }

        private void FeedRange_PartitionKey_Validate(PartitionKey partitionKey)
        {
            string continuationToken = "TBD";
            string containerRid = Guid.NewGuid().ToString();
            FeedRangePartitionKey feedTokenPartitionKey = new FeedRangePartitionKey(partitionKey);
            FeedRangeCompositeContinuation feedRangeSimpleContinuation = new FeedRangeCompositeContinuation(containerRid, feedTokenPartitionKey, new List<Documents.Routing.Range<string>>() { Documents.Routing.Range<string>.GetEmptyRange("AA") },continuationToken);
            string serialized = feedRangeSimpleContinuation.ToString();
            Assert.IsTrue(FeedRangeContinuation.TryParse(serialized, out FeedRangeContinuation deserialized));
            FeedRangeCompositeContinuation deserializedContinuation = deserialized as FeedRangeCompositeContinuation;
            FeedRangePartitionKey deserializedFeedRange = deserializedContinuation.FeedRange as FeedRangePartitionKey;
            Assert.AreEqual(feedTokenPartitionKey.PartitionKey.ToJsonString(), deserializedFeedRange.PartitionKey.ToJsonString());
            Assert.AreEqual(continuationToken, deserializedContinuation.GetContinuation());
        }
    }
}
