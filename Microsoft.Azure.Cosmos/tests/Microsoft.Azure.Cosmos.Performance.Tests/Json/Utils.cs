﻿namespace Microsoft.Azure.Cosmos.Performance.Tests
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Azure.Cosmos.Json;
    using Microsoft.Azure.Cosmos.Tests.Json;

    internal static class Utils
    {
        public static void DrainReader(IJsonReader jsonReader, bool materializeValue)
        {
            if (jsonReader == null)
            {
                throw new ArgumentNullException(nameof(jsonReader));
            }

            while (jsonReader.Read())
            {
                // Materialize the value
                switch (jsonReader.CurrentTokenType)
                {
                    case JsonTokenType.BeginArray:
                    case JsonTokenType.EndArray:
                    case JsonTokenType.BeginObject:
                    case JsonTokenType.EndObject:
                    case JsonTokenType.Null:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        // Single byte tokens
                        break;

                    case JsonTokenType.String:
                    case JsonTokenType.FieldName:
                        if (materializeValue)
                        {
                            string stringValue = jsonReader.GetStringValue();
                        }
                        break;

                    case JsonTokenType.Number:
                        if (materializeValue)
                        {
                            Number64 number64Value = jsonReader.GetNumberValue();
                        }
                        break;

                    case JsonTokenType.Int8:
                        if (materializeValue)
                        {
                            sbyte int8Value = jsonReader.GetInt8Value();
                        }
                        break;

                    case JsonTokenType.Int16:
                        if (materializeValue)
                        {
                            short int16Value = jsonReader.GetInt16Value();
                        }
                        break;

                    case JsonTokenType.Int32:
                        if (materializeValue)
                        {
                            int int32Value = jsonReader.GetInt32Value();
                        }
                        break;

                    case JsonTokenType.Int64:
                        if (materializeValue)
                        {
                            long int64Value = jsonReader.GetInt64Value();
                        }
                        break;

                    case JsonTokenType.UInt32:
                        if (materializeValue)
                        {
                            uint uInt32Value = jsonReader.GetUInt32Value();
                        }
                        break;

                    case JsonTokenType.Float32:
                        if (materializeValue)
                        {
                            float float32Value = jsonReader.GetFloat32Value();
                        }
                        break;

                    case JsonTokenType.Float64:
                        if (materializeValue)
                        {
                            double doubleValue = jsonReader.GetFloat64Value();
                        }
                        break;

                    case JsonTokenType.Guid:
                        if (materializeValue)
                        {
                            Guid guidValue = jsonReader.GetGuidValue();
                        }
                        break;

                    case JsonTokenType.Binary:
                        if (materializeValue)
                        {
                            ReadOnlyMemory<byte> binaryValue = jsonReader.GetBinaryValue();
                        }
                        break;

                    default:
                        throw new ArgumentException("$Unknown token type.");
                }
            }
        }

        public static void DrainNavigator(IJsonNavigator jsonNavigator)
        {
            if (jsonNavigator == null)
            {
                throw new ArgumentNullException(nameof(jsonNavigator));
            }

            static void Navigate(IJsonNavigator navigator, IJsonNavigatorNode node)
            {
                switch (navigator.GetNodeType(node))
                {
                    case JsonNodeType.Null:
                    case JsonNodeType.False:
                    case JsonNodeType.True:
                    case JsonNodeType.Number64:
                    case JsonNodeType.Int8:
                    case JsonNodeType.Int16:
                    case JsonNodeType.Int32:
                    case JsonNodeType.Int64:
                    case JsonNodeType.UInt32:
                    case JsonNodeType.Float32:
                    case JsonNodeType.Float64:
                    case JsonNodeType.String:
                    case JsonNodeType.Binary:
                    case JsonNodeType.Guid:
                        break;

                    case JsonNodeType.Array:
                        foreach (IJsonNavigatorNode arrayItem in navigator.GetArrayItems(node))
                        {
                            Navigate(navigator, arrayItem);
                        }
                        break;

                    case JsonNodeType.Object:
                        foreach (ObjectProperty objectProperty in navigator.GetObjectProperties(node))
                        {
                            IJsonNavigatorNode nameNode = objectProperty.NameNode;
                            IJsonNavigatorNode valueNode = objectProperty.ValueNode;
                            Navigate(navigator, valueNode);
                        }
                        break;

                    default:
                        throw new ArgumentOutOfRangeException($"Unknown {nameof(JsonNodeType)}: '{navigator.GetNodeType(node)}.'");
                }
            }

            Navigate(jsonNavigator, jsonNavigator.GetRootNode());
        }

        public static void FlushToWriter(IJsonWriter jsonWriter, IReadOnlyList<JsonToken> tokensToWrite)
        {
            foreach (JsonToken token in tokensToWrite)
            {
                switch (token.JsonTokenType)
                {
                    case JsonTokenType.BeginArray:
                        jsonWriter.WriteArrayStart();
                        break;
                    case JsonTokenType.EndArray:
                        jsonWriter.WriteArrayEnd();
                        break;
                    case JsonTokenType.BeginObject:
                        jsonWriter.WriteObjectStart();
                        break;
                    case JsonTokenType.EndObject:
                        jsonWriter.WriteObjectEnd();
                        break;
                    case JsonTokenType.String:
                        string stringValue = (token as JsonStringToken).Value;
                        jsonWriter.WriteStringValue(stringValue);
                        break;
                    case JsonTokenType.Number:
                        Number64 numberValue = (token as JsonNumberToken).Value;
                        jsonWriter.WriteNumber64Value(numberValue);
                        break;
                    case JsonTokenType.True:
                        jsonWriter.WriteBoolValue(true);
                        break;
                    case JsonTokenType.False:
                        jsonWriter.WriteBoolValue(false);
                        break;
                    case JsonTokenType.Null:
                        jsonWriter.WriteNullValue();
                        break;
                    case JsonTokenType.FieldName:
                        string fieldNameValue = (token as JsonFieldNameToken).Value;
                        jsonWriter.WriteFieldName(fieldNameValue);
                        break;
                    case JsonTokenType.NotStarted:
                    default:
                        throw new ArgumentException("invalid jsontoken");
                }
            }
        }

        public static JsonToken[] Tokenize(ReadOnlyMemory<byte> buffer)
        {
            IJsonReader jsonReader = JsonReader.Create(buffer);
            List<JsonToken> tokens = new List<JsonToken>();

            static void TokenizeInternal(IJsonReader jsonReader, List<JsonToken> tokenList)
            {
                while (jsonReader.Read())
                {
                    switch (jsonReader.CurrentTokenType)
                    {
                        case JsonTokenType.NotStarted:
                            throw new ArgumentException(string.Format("Got an unexpected JsonTokenType: {0} as an expected token type", jsonReader.CurrentTokenType));
                        case JsonTokenType.BeginArray:
                            tokenList.Add(JsonToken.ArrayStart());
                            break;
                        case JsonTokenType.EndArray:
                            tokenList.Add(JsonToken.ArrayEnd());
                            break;
                        case JsonTokenType.BeginObject:
                            tokenList.Add(JsonToken.ObjectStart());
                            break;
                        case JsonTokenType.EndObject:
                            tokenList.Add(JsonToken.ObjectEnd());
                            break;
                        case JsonTokenType.String:
                            tokenList.Add(JsonToken.String(jsonReader.GetStringValue()));
                            break;
                        case JsonTokenType.Number:
                            tokenList.Add(JsonToken.Number(jsonReader.GetNumberValue()));
                            break;
                        case JsonTokenType.True:
                            tokenList.Add(JsonToken.Boolean(true));
                            break;
                        case JsonTokenType.False:
                            tokenList.Add(JsonToken.Boolean(false));
                            break;
                        case JsonTokenType.Null:
                            tokenList.Add(JsonToken.Null());
                            break;
                        case JsonTokenType.FieldName:
                            tokenList.Add(JsonToken.FieldName(jsonReader.GetStringValue()));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException($"Unknown {nameof(JsonTokenType)}: '{jsonReader.CurrentTokenType}'.");
                    }
                }
            }

            TokenizeInternal(jsonReader, tokens);
            return tokens.ToArray();
        }
    }
}
