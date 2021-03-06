﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.DotNet.Interactive.Commands;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Interactive.Server
{
    public abstract class KernelCommandEnvelope : IKernelCommandEnvelope
    {
        private static readonly ConcurrentDictionary<Type, Func<IKernelCommand, IKernelCommandEnvelope>> _envelopeFactories =
            new ConcurrentDictionary<Type, Func<IKernelCommand, IKernelCommandEnvelope>>();

        private static readonly Dictionary<string, Type> _envelopeTypesByCommandTypeName;

        private static readonly Dictionary<string, Type> _commandTypesByCommandTypeName;

        static KernelCommandEnvelope()
        {
            _envelopeTypesByCommandTypeName = new Dictionary<string, Type>
            {
                [nameof(AddPackage)] = typeof(KernelCommandEnvelope<AddPackage>),
                [nameof(CancelCurrentCommand)] = typeof(KernelCommandEnvelope<CancelCurrentCommand>),
                [nameof(DisplayError)] = typeof(KernelCommandEnvelope<DisplayError>),
                [nameof(DisplayValue)] = typeof(KernelCommandEnvelope<DisplayValue>),
                [nameof(LoadExtension)] = typeof(KernelCommandEnvelope<LoadExtension>),
                [nameof(LoadExtensionsInDirectory)] = typeof(KernelCommandEnvelope<LoadExtensionsInDirectory>),
                [nameof(RestoreNugetDirective)] = typeof(KernelCommandEnvelope<RestoreNugetDirective>),
                [nameof(RequestCompletion)] = typeof(KernelCommandEnvelope<RequestCompletion>),
                [nameof(RequestDiagnostics)] = typeof(KernelCommandEnvelope<RequestDiagnostics>),
                [nameof(RunDirective)] = typeof(KernelCommandEnvelope<RunDirective>),
                [nameof(SubmitCode)] = typeof(KernelCommandEnvelope<SubmitCode>),
                [nameof(UpdateDisplayedValue)] = typeof(KernelCommandEnvelope<UpdateDisplayedValue>)
            };

            _commandTypesByCommandTypeName = _envelopeTypesByCommandTypeName
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.GetGenericArguments()[0]);
        }

        internal static Type CommandTypeByName(string name) => _commandTypesByCommandTypeName[name];

        private readonly IKernelCommand _command;

        protected KernelCommandEnvelope(IKernelCommand command)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
        }

        public abstract string CommandType { get; }

        public string Token { get; }

        IKernelCommand IKernelCommandEnvelope.Command => _command;

        public static IKernelCommandEnvelope Create(IKernelCommand command)
        {
            var factory = _envelopeFactories.GetOrAdd(
                command.GetType(),
                commandType =>
                {
                    var genericType = _envelopeTypesByCommandTypeName[command.GetType().Name];

                    var constructor = genericType.GetConstructors().Single();

                    var commandParameter = Expression.Parameter(
                        typeof(IKernelCommand),
                        "c");

                    var newExpression = Expression.New(
                        constructor,
                        Expression.Convert(commandParameter, commandType));

                    var expression = Expression.Lambda<Func<IKernelCommand, IKernelCommandEnvelope>>(
                        newExpression,
                        commandParameter);

                    return expression.Compile();
                });

            return factory(command);
        }

        public static IKernelCommandEnvelope Deserialize(string json)
        {
            var jsonObject = JObject.Parse(json);

            return Deserialize(jsonObject);
        }

        internal static IKernelCommandEnvelope Deserialize(JToken json)
        {
            var commandJson = json["command"];

            var commandTypeName = json["commandType"].Value<string>();

            if (commandTypeName == null)
            {
                return null;
            }

            var commandType = CommandTypeByName(commandTypeName);

            var command = (IKernelCommand) commandJson.ToObject(commandType, Serializer.JsonSerializer);

            return Create(command);
        }

        public static string Serialize(IKernelCommandEnvelope envelope)
        {
            var serializationModel = new SerializationModel
            {
                command = envelope.Command,
                commandType = envelope.CommandType,
                token = envelope.Token
            };

            return JsonConvert.SerializeObject(
                serializationModel,
                Serializer.JsonSerializerSettings);
        }

        internal class SerializationModel
        {
            public string token { get; set; }

            public string commandType { get; set; }

            public object command { get; set; }
        }
    }
}