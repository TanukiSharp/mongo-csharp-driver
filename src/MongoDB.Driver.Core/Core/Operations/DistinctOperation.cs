/* Copyright 2013-2015 MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver.Core.Bindings;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.WireProtocol.Messages.Encoders;

namespace MongoDB.Driver.Core.Operations
{
    /// <summary>
    /// Represents a distinct operation.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    public class DistinctOperation<TValue> : IReadOperation<IAsyncCursor<TValue>>
    {
        // fields
        private CollectionNamespace _collectionNamespace;
        private BsonDocument _filter;
        private string _fieldName;
        private TimeSpan? _maxTime;
        private MessageEncoderSettings _messageEncoderSettings;
        private ReadConcern _readConcern = ReadConcern.Default;
        private IBsonSerializer<TValue> _valueSerializer;

        // constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="DistinctOperation{TValue}"/> class.
        /// </summary>
        /// <param name="collectionNamespace">The collection namespace.</param>
        /// <param name="valueSerializer">The value serializer.</param>
        /// <param name="fieldName">The name of the field.</param>
        /// <param name="messageEncoderSettings">The message encoder settings.</param>
        public DistinctOperation(CollectionNamespace collectionNamespace, IBsonSerializer<TValue> valueSerializer, string fieldName, MessageEncoderSettings messageEncoderSettings)
        {
            _collectionNamespace = Ensure.IsNotNull(collectionNamespace, nameof(collectionNamespace));
            _valueSerializer = Ensure.IsNotNull(valueSerializer, nameof(valueSerializer));
            _fieldName = Ensure.IsNotNullOrEmpty(fieldName, nameof(fieldName));
            _messageEncoderSettings = Ensure.IsNotNull(messageEncoderSettings, nameof(messageEncoderSettings));
        }

        // properties
        /// <summary>
        /// Gets the collection namespace.
        /// </summary>
        /// <value>
        /// The collection namespace.
        /// </value>
        public CollectionNamespace CollectionNamespace
        {
            get { return _collectionNamespace; }
        }

        /// <summary>
        /// Gets or sets the filter.
        /// </summary>
        /// <value>
        /// The filter.
        /// </value>
        public BsonDocument Filter
        {
            get { return _filter; }
            set { _filter = value; }
        }

        /// <summary>
        /// Gets the name of the field.
        /// </summary>
        /// <value>
        /// The name of the field.
        /// </value>
        public string FieldName
        {
            get { return _fieldName; }
        }

        /// <summary>
        /// Gets or sets the maximum time the server should spend on this operation.
        /// </summary>
        /// <value>
        /// The maximum time the server should spend on this operation.
        /// </value>
        public TimeSpan? MaxTime
        {
            get { return _maxTime; }
            set { _maxTime = Ensure.IsNullOrInfiniteOrGreaterThanOrEqualToZero(value, nameof(value)); }
        }

        /// <summary>
        /// Gets the message encoder settings.
        /// </summary>
        /// <value>
        /// The message encoder settings.
        /// </value>
        public MessageEncoderSettings MessageEncoderSettings
        {
            get { return _messageEncoderSettings; }
        }

        /// <summary>
        /// Gets or sets the read concern.
        /// </summary>
        /// <value>
        /// The read concern.
        /// </value>
        public ReadConcern ReadConcern
        {
            get { return _readConcern; }
            set { _readConcern = Ensure.IsNotNull(value, nameof(value)); }
        }

        /// <summary>
        /// Gets the value serializer.
        /// </summary>
        /// <value>
        /// The value serializer.
        /// </value>
        public IBsonSerializer<TValue> ValueSerializer
        {
            get { return _valueSerializer; }
        }

        // public methods
        /// <inheritdoc/>
        public IAsyncCursor<TValue> Execute(IReadBinding binding, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(binding, nameof(binding));
            using (var channelSource = binding.GetReadChannelSource(cancellationToken))
            using (var channel = channelSource.GetChannel(cancellationToken))
            using (var channelBinding = new ChannelReadBinding(channelSource.Server, channel, binding.ReadPreference))
            {
                var operation = CreateOperation(channel.ConnectionDescription.ServerVersion);
                var values = operation.Execute(channelBinding, cancellationToken);
                return new SingleBatchAsyncCursor<TValue>(values);
            }
        }

        /// <inheritdoc/>
        public async Task<IAsyncCursor<TValue>> ExecuteAsync(IReadBinding binding, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(binding, nameof(binding));
            using (var channelSource = await binding.GetReadChannelSourceAsync(cancellationToken).ConfigureAwait(false))
            using (var channel = await channelSource.GetChannelAsync(cancellationToken).ConfigureAwait(false))
            using (var channelBinding = new ChannelReadBinding(channelSource.Server, channel, binding.ReadPreference))
            {
                var operation = CreateOperation(channel.ConnectionDescription.ServerVersion);
                var values = await operation.ExecuteAsync(channelBinding, cancellationToken).ConfigureAwait(false);
                return new SingleBatchAsyncCursor<TValue>(values);
            }
        }

        // private methods
        internal BsonDocument CreateCommand(SemanticVersion serverVersion)
        {
            _readConcern.ThrowIfNotSupported(serverVersion);

            return new BsonDocument
            {
                { "distinct", _collectionNamespace.CollectionName },
                { "key", _fieldName },
                { "query", _filter, _filter != null },
                { "maxTimeMS", () => _maxTime.Value.TotalMilliseconds, _maxTime.HasValue },
                { "readConcern", () => _readConcern.ToBsonDocument(), !_readConcern.IsServerDefault }
           };
        }

        private ReadCommandOperation<TValue[]> CreateOperation(SemanticVersion serverVersion)
        {
            var command = CreateCommand(serverVersion);
            var valueArraySerializer = new ArraySerializer<TValue>(_valueSerializer);
            var resultSerializer = new ElementDeserializer<TValue[]>("values", valueArraySerializer);
            return new ReadCommandOperation<TValue[]>(_collectionNamespace.DatabaseNamespace, command, resultSerializer, _messageEncoderSettings);
        }
    }
}
