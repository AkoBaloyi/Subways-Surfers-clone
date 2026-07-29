using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ContractOwnershipTests
    {
        [Test]
        public void PublicContractSurfaceHasZeroConcreteRachelOrLuckyDependencies_Requirements_1_3_1_4_1_6_7_12_14_11_14_12()
        {
            var runtimeAssembly = typeof(IPlayerCommands).Assembly;
            var forbiddenReferences = runtimeAssembly.GetReferencedAssemblies()
                .Where(reference => IsProductionTeamName(reference.Name))
                .Select(reference => reference.FullName)
                .ToArray();
            Assert.That(forbiddenReferences, Is.Empty);

            var forbiddenSurface = runtimeAssembly.GetExportedTypes()
                .Where(type => type.Namespace != null && type.Namespace.StartsWith("SubwaySurfers.Player", StringComparison.Ordinal))
                .SelectMany(ReferencedSurfaceTypes)
                .Where(type => IsProductionTeamName(type.FullName ?? type.Name))
                .Select(type => type.FullName)
                .Distinct()
                .ToArray();
            Assert.That(forbiddenSurface, Is.Empty,
                "Player contracts must use player-owned abstractions rather than concrete production team types.");
        }

        private static IEnumerable<Type> ReferencedSurfaceTypes(Type type)
        {
            yield return type;
            foreach (var property in type.GetProperties()) yield return property.PropertyType;
            foreach (var eventInfo in type.GetEvents()) yield return eventInfo.EventHandlerType;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
            {
                yield return method.ReturnType;
                foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
            }
            foreach (var constructor in type.GetConstructors())
                foreach (var parameter in constructor.GetParameters()) yield return parameter.ParameterType;
        }

        private static bool IsProductionTeamName(string name)
        {
            return name.IndexOf("Rachel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Lucky", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
