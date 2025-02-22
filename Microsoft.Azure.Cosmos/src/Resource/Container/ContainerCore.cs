﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Query.Core.QueryClient;
    using Microsoft.Azure.Cosmos.Resource.CosmosExceptions;
    using Microsoft.Azure.Cosmos.Routing;
    using Microsoft.Azure.Cosmos.Scripts;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Routing;

    /// <summary>
    /// Operations for reading, replacing, or deleting a specific, existing container by id.
    /// 
    /// <see cref="Cosmos.Database"/> for creating new containers, and reading/querying all containers;
    /// </summary>
    internal partial class ContainerCore : ContainerInternal
    {
        private readonly Lazy<BatchAsyncContainerExecutor> lazyBatchExecutor;

        protected ContainerCore(
            CosmosClientContext clientContext,
            DatabaseInternal database,
            string containerId,
            CosmosQueryClient cosmosQueryClient = null)
        {
            this.Id = containerId;
            this.ClientContext = clientContext;
            this.LinkUri = clientContext.CreateLink(
                parentLink: database.LinkUri.OriginalString,
                uriPathSegment: Paths.CollectionsPathSegment,
                id: containerId);

            this.Database = database;
            this.Conflicts = new ConflictsInlineCore(this.ClientContext, this);
            this.Scripts = new ScriptsInlineCore(this, this.ClientContext);
            this.cachedUriSegmentWithoutId = this.GetResourceSegmentUriWithoutId();
            this.queryClient = cosmosQueryClient ?? new CosmosQueryClientCore(this.ClientContext, this);
            this.lazyBatchExecutor = new Lazy<BatchAsyncContainerExecutor>(() => this.ClientContext.GetExecutorForContainer(this));
        }

        public override string Id { get; }

        public override Database Database { get; }

        public override Uri LinkUri { get; }

        public override CosmosClientContext ClientContext { get; }

        public override BatchAsyncContainerExecutor BatchExecutor => this.lazyBatchExecutor.Value;

        public override Conflicts Conflicts { get; }

        public override Scripts.Scripts Scripts { get; }

        public override async Task<ContainerResponse> ReadContainerAsync(
            ContainerRequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ResponseMessage response = await this.ReadContainerStreamAsync(
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);

            return this.ClientContext.ResponseFactory.CreateContainerResponse(this, response);
        }

        public override async Task<ContainerResponse> ReplaceContainerAsync(
            ContainerProperties containerProperties,
            ContainerRequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (containerProperties == null)
            {
                throw new ArgumentNullException(nameof(containerProperties));
            }

            this.ClientContext.ValidateResource(containerProperties.Id);
            ResponseMessage response = await this.ReplaceStreamInternalAsync(
                streamPayload: this.ClientContext.SerializerCore.ToStream(containerProperties),
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);

            return this.ClientContext.ResponseFactory.CreateContainerResponse(this, response);
        }

        public override async Task<ContainerResponse> DeleteContainerAsync(
            ContainerRequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ResponseMessage response = await this.DeleteContainerStreamAsync(
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);

            return this.ClientContext.ResponseFactory.CreateContainerResponse(this, response);
        }

        public override async Task<int?> ReadThroughputAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThroughputResponse response = await this.ReadThroughputIfExistsAsync(null, cancellationToken);
            return response.Resource?.Throughput;
        }

        public override async Task<ThroughputResponse> ReadThroughputAsync(
            RequestOptions requestOptions,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string rid = await this.GetRIDAsync(cancellationToken);
            CosmosOffers cosmosOffers = new CosmosOffers(this.ClientContext);
            return await cosmosOffers.ReadThroughputAsync(rid, requestOptions, cancellationToken);
        }

        public override async Task<ThroughputResponse> ReadThroughputIfExistsAsync(
            RequestOptions requestOptions,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string rid = await this.GetRIDAsync(cancellationToken);
            CosmosOffers cosmosOffers = new CosmosOffers(this.ClientContext);
            return await cosmosOffers.ReadThroughputIfExistsAsync(rid, requestOptions, cancellationToken);
        }

        public override async Task<ThroughputResponse> ReplaceThroughputAsync(
            int throughput,
            RequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string rid = await this.GetRIDAsync(cancellationToken);

            CosmosOffers cosmosOffers = new CosmosOffers(this.ClientContext);
            return await cosmosOffers.ReplaceThroughputAsync(
                targetRID: rid,
                throughput: throughput,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public override async Task<ThroughputResponse> ReplaceThroughputIfExistsAsync(
            ThroughputProperties throughput,
            RequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string rid = await this.GetRIDAsync(cancellationToken);

            CosmosOffers cosmosOffers = new CosmosOffers(this.ClientContext);
            return await cosmosOffers.ReplaceThroughputPropertiesIfExistsAsync(
                targetRID: rid,
                throughputProperties: throughput,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public override async Task<ThroughputResponse> ReplaceThroughputAsync(
            ThroughputProperties throughputProperties,
            RequestOptions requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            string rid = await this.GetRIDAsync(cancellationToken);
            CosmosOffers cosmosOffers = new CosmosOffers(this.ClientContext);
            return await cosmosOffers.ReplaceThroughputPropertiesAsync(
                rid,
                throughputProperties,
                requestOptions,
                cancellationToken);
        }

        public override Task<ResponseMessage> DeleteContainerStreamAsync(
            ContainerRequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.ProcessStreamAsync(
               streamPayload: null,
               operationType: OperationType.Delete,
               requestOptions: requestOptions,
               cancellationToken: cancellationToken);
        }

        public override Task<ResponseMessage> ReadContainerStreamAsync(
            ContainerRequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.ProcessStreamAsync(
                streamPayload: null,
                operationType: OperationType.Read,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public override Task<ResponseMessage> ReplaceContainerStreamAsync(
            ContainerProperties containerProperties,
            ContainerRequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (containerProperties == null)
            {
                throw new ArgumentNullException(nameof(containerProperties));
            }

            this.ClientContext.ValidateResource(containerProperties.Id);
            return this.ReplaceStreamInternalAsync(
                streamPayload: this.ClientContext.SerializerCore.ToStream(containerProperties),
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public override async Task<IReadOnlyList<FeedRange>> GetFeedRangesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            PartitionKeyRangeCache partitionKeyRangeCache = await this.ClientContext.DocumentClient.GetPartitionKeyRangeCacheAsync();
            string containerRId = await this.GetRIDAsync(cancellationToken);
            IReadOnlyList<PartitionKeyRange> partitionKeyRanges = await partitionKeyRangeCache.TryGetOverlappingRangesAsync(
                        containerRId,
                        new Range<string>(
                            PartitionKeyInternal.MinimumInclusiveEffectivePartitionKey,
                            PartitionKeyInternal.MaximumExclusiveEffectivePartitionKey,
                            isMinInclusive: true,
                            isMaxInclusive: false),
                        forceRefresh: true);
            List<FeedRangeEPK> feedTokens = new List<FeedRangeEPK>(partitionKeyRanges.Count);
            foreach (PartitionKeyRange partitionKeyRange in partitionKeyRanges)
            {
                feedTokens.Add(new FeedRangeEPK(partitionKeyRange.ToRange()));
            }

            return feedTokens;
        }

        public override FeedIterator GetChangeFeedStreamIterator(
            string continuationToken = null,
            ChangeFeedRequestOptions changeFeedRequestOptions = null)
        {
            return ChangeFeedIteratorCore.Create(
                container: this,
                feedRangeInternal: null,
                continuation: continuationToken,
                changeFeedRequestOptions: changeFeedRequestOptions);
        }

        public override FeedIterator GetChangeFeedStreamIterator(
            FeedRange feedRange,
            ChangeFeedRequestOptions changeFeedRequestOptions = null)
        {
            FeedRangeInternal feedRangeInternal = feedRange as FeedRangeInternal;
            return ChangeFeedIteratorCore.Create(
                container: this,
                feedRangeInternal: feedRangeInternal,
                continuation: null,
                changeFeedRequestOptions: changeFeedRequestOptions);
        }
        public override FeedIterator GetChangeFeedStreamIterator(
            PartitionKey partitionKey,
            ChangeFeedRequestOptions changeFeedRequestOptions = null)
        {
            return ChangeFeedIteratorCore.Create(
                container: this,
                feedRangeInternal: new FeedRangePartitionKey(partitionKey),
                continuation: null,
                changeFeedRequestOptions: changeFeedRequestOptions);
        }

        public override FeedIterator<T> GetChangeFeedIterator<T>(
            string continuationToken = null,
            ChangeFeedRequestOptions changeFeedRequestOptions = null)
        {
            ChangeFeedIteratorCore changeFeedIteratorCore = ChangeFeedIteratorCore.Create(
                container: this,
                feedRangeInternal: null,
                continuation: continuationToken,
                changeFeedRequestOptions: changeFeedRequestOptions);

            return new FeedIteratorCore<T>(changeFeedIteratorCore, responseCreator: this.ClientContext.ResponseFactory.CreateChangeFeedUserTypeResponse<T>);
        }

        public override FeedIterator<T> GetChangeFeedIterator<T>(
            FeedRange feedRange,
            ChangeFeedRequestOptions changeFeedRequestOptions = null)
        {
            FeedRangeInternal feedRangeInternal = feedRange as FeedRangeInternal;
            ChangeFeedIteratorCore changeFeedIteratorCore = ChangeFeedIteratorCore.Create(
                container: this,
                feedRangeInternal: feedRangeInternal,
                continuation: null,
                changeFeedRequestOptions: changeFeedRequestOptions);

            return new FeedIteratorCore<T>(changeFeedIteratorCore, responseCreator: this.ClientContext.ResponseFactory.CreateChangeFeedUserTypeResponse<T>);
        }
        public override FeedIterator<T> GetChangeFeedIterator<T>(
            PartitionKey partitionKey,
            ChangeFeedRequestOptions changeFeedRequestOptions = null)
        {
            ChangeFeedIteratorCore changeFeedIteratorCore = ChangeFeedIteratorCore.Create(
                container: this,
                feedRangeInternal: new FeedRangePartitionKey(partitionKey),
                continuation: null,
                changeFeedRequestOptions: changeFeedRequestOptions);

            return new FeedIteratorCore<T>(changeFeedIteratorCore, responseCreator: this.ClientContext.ResponseFactory.CreateChangeFeedUserTypeResponse<T>);
        }
        public override async Task<IEnumerable<string>> GetPartitionKeyRangesAsync(
            FeedRange feedRange,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            IRoutingMapProvider routingMapProvider = await this.ClientContext.DocumentClient.GetPartitionKeyRangeCacheAsync();
            string containerRid = await this.GetRIDAsync(cancellationToken);
            PartitionKeyDefinition partitionKeyDefinition = await this.GetPartitionKeyDefinitionAsync(cancellationToken);

            if (!(feedRange is FeedRangeInternal feedTokenInternal))
            {
                throw new ArgumentException(nameof(feedRange), ClientResources.FeedToken_UnrecognizedFeedToken);
            }

            return await feedTokenInternal.GetPartitionKeyRangesAsync(routingMapProvider, containerRid, partitionKeyDefinition, cancellationToken);
        }

        /// <summary>
        /// Gets the container's Properties by using the internal cache.
        /// In case the cache does not have information about this container, it may end up making a server call to fetch the data.
        /// </summary>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> representing request cancellation.</param>
        /// <returns>A <see cref="Task"/> containing the <see cref="ContainerProperties"/> for this container.</returns>
        public override async Task<ContainerProperties> GetCachedContainerPropertiesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ClientCollectionCache collectionCache = await this.ClientContext.DocumentClient.GetCollectionCacheAsync();
            try
            {
                return await collectionCache.ResolveByNameAsync(HttpConstants.Versions.CurrentVersion, this.LinkUri.OriginalString, cancellationToken);
            }
            catch (DocumentClientException ex)
            {
                throw CosmosExceptionFactory.Create(
                    dce: ex,
                    diagnosticsContext: null);
            }
        }

        // Name based look-up, needs re-computation and can't be cached
        public override async Task<string> GetRIDAsync(CancellationToken cancellationToken)
        {
            ContainerProperties containerProperties = await this.GetCachedContainerPropertiesAsync(cancellationToken);
            return containerProperties?.ResourceId;
        }

        public override Task<PartitionKeyDefinition> GetPartitionKeyDefinitionAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.GetCachedContainerPropertiesAsync(cancellationToken)
                            .ContinueWith(containerPropertiesTask => containerPropertiesTask.Result?.PartitionKey, cancellationToken);
        }

        /// <summary>
        /// Used by typed API only. Exceptions are allowed.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>Returns the partition key path</returns>
        public override async Task<string[]> GetPartitionKeyPathTokensAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ContainerProperties containerProperties = await this.GetCachedContainerPropertiesAsync(cancellationToken);
            if (containerProperties == null)
            {
                throw new ArgumentOutOfRangeException($"Container {this.LinkUri.ToString()} not found");
            }

            if (containerProperties.PartitionKey?.Paths == null)
            {
                throw new ArgumentOutOfRangeException($"Partition key not defined for container {this.LinkUri.ToString()}");
            }

            return containerProperties.PartitionKeyPathTokens;
        }

        /// <summary>
        /// Instantiates a new instance of the <see cref="PartitionKeyInternal"/> object.
        /// </summary>
        /// <remarks>
        /// The function selects the right partition key constant for inserting documents that don't have
        /// a value for partition key. The constant selection is based on whether the collection is migrated
        /// or user partitioned
        /// 
        /// For non-existing container will throw <see cref="DocumentClientException"/> with 404 as status code
        /// </remarks>
        public override async Task<PartitionKeyInternal> GetNonePartitionKeyValueAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ContainerProperties containerProperties = await this.GetCachedContainerPropertiesAsync(cancellationToken);
            return containerProperties.GetNoneValue();
        }

        public override Task<CollectionRoutingMap> GetRoutingMapAsync(CancellationToken cancellationToken)
        {
            string collectionRID = null;
            return this.GetRIDAsync(cancellationToken)
                .ContinueWith(ridTask =>
                {
                    collectionRID = ridTask.Result;
                    return this.ClientContext.Client.DocumentClient.GetPartitionKeyRangeCacheAsync();
                })
                .Unwrap()
                .ContinueWith(partitionKeyRangeCachetask =>
                {
                    PartitionKeyRangeCache partitionKeyRangeCache = partitionKeyRangeCachetask.Result;
                    return partitionKeyRangeCache.TryLookupAsync(
                            collectionRID,
                            null,
                            null,
                            cancellationToken);
                })
                .Unwrap();
        }

        private Task<ResponseMessage> ReplaceStreamInternalAsync(
            Stream streamPayload,
            ContainerRequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.ProcessStreamAsync(
                streamPayload: streamPayload,
                operationType: OperationType.Replace,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        private Task<ResponseMessage> ProcessStreamAsync(
            Stream streamPayload,
            OperationType operationType,
            ContainerRequestOptions requestOptions = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.ProcessResourceOperationStreamAsync(
                streamPayload: streamPayload,
                operationType: operationType,
                linkUri: this.LinkUri,
                resourceType: ResourceType.Collection,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        private Task<ResponseMessage> ProcessResourceOperationStreamAsync(
           Stream streamPayload,
           OperationType operationType,
           Uri linkUri,
           ResourceType resourceType,
           RequestOptions requestOptions = null,
           CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.ClientContext.ProcessResourceOperationStreamAsync(
              resourceUri: linkUri,
              resourceType: resourceType,
              operationType: operationType,
              cosmosContainerCore: null,
              partitionKey: null,
              streamPayload: streamPayload,
              requestOptions: requestOptions,
              requestEnricher: null,
              diagnosticsContext: null,
              cancellationToken: cancellationToken);
        }
    }
}
