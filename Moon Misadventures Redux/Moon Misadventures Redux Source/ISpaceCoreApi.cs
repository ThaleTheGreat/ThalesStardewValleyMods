using System;
using System.Reflection;

namespace SpaceShared.APIs
{
    public interface ISpaceCoreApi
    {
        void RegisterSerializerType(Type type);
        void RegisterCustomProperty(Type declaringType, string name, Type propertyType, MethodInfo getter, MethodInfo setter);
    }
}
