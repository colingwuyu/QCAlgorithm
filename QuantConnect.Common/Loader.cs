﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals, V0.1
 * Safe DLL Loader -
*/

/**********************************************************
* USING NAMESPACES
**********************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq.Expressions;

//QuantConnect Project Libraries:
using QuantConnect.Logging;

namespace QuantConnect {

    /******************************************************** 
    * CLASS DEFINITIONS
    *********************************************************/
    /// <summary>
    /// Loader creates and manages the memory and exception space of the algorithm, ensuring if it explodes the QC Node is instact.
    /// </summary>
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class Loader : MarshalByRefObject {

        /******************************************************** 
        * CLASS VARIABLES
        *********************************************************/
        /// <summary>
        /// Memory space of the user algorithm
        /// </summary>
        public AppDomain appDomain;


        /******************************************************** 
        * CLASS METHODS
        *********************************************************/
        /// <summary>
        /// Creates a new instance of the class library in a new AppDomain, safely.
        /// </summary>
        /// <param name="assemblyPath">Location of the DLL</param>
        /// <param name="baseTypeName">QCAlgorithm base type</param>
        /// <param name="algorithmInstance">Output algorithm instance</param>
        /// <param name="errorMessage">Output error message on failure</param>
        /// <returns>bool success</returns>        
        public bool CreateInstance<T>(string assemblyPath, string baseTypeName, out T algorithmInstance, out string errorMessage) {

            //Default initialisation of Assembly.
            algorithmInstance = default(T);
            errorMessage = "";

            //First most basic check:
            if (!File.Exists(assemblyPath)) 
            {
                return false;
            }

            //Create a new app domain with a generic name.
            CreateAppDomain();

            try {

                //Load the assembly:
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                if (assembly == null)
                {
                    Log.Error("Loader.CreateInstance(): Assembly is null");
                    return false;
                }

                //Get the list of extention classes in the library: 
                List<string> types = GetExtendedTypeNames(assembly, baseTypeName);
                Log.Trace("Loader.CreateInstance(): Assembly types: " + string.Join(",", types));

                //No extensions, nothing to load.
                if (types.Count == 0)
                {
                    Log.Error("Loader.CreateInstance(): Types array empty, no algorithm type found.");
                    return false;
                } 
                else
                {
                    //Load the assembly into this AppDomain:
                    algorithmInstance = (T)assembly.CreateInstance(types[0], true);
                    //Load into another appDomain - 10x slower because of serialization.
                    //algorithmInstance = (T)appDomain.CreateInstanceFromAndUnwrap(assemblyPath, lTypes[0]);
                }
            }
            catch (ReflectionTypeLoadException err) 
            {
                Log.Error("QC.Loader.CreateInstance(1): " + err.LoaderExceptions[0].ToString());
                if (err.InnerException != null) errorMessage = err.InnerException.Message;
            } 
            catch (Exception err)
            {
                Log.Error("QC.Loader.CreateInstance(2): " + err.Message);
                if (err.InnerException != null) errorMessage = err.InnerException.Message;
            }

            //Successful load.
            if (algorithmInstance != null) 
            {
                return true;
            } 
            else 
            {
                return false;
            }
        }


        /// <summary>
        /// Fast Object Creator from Generic Type: 
        /// Modified from http://rogeralsing.com/2008/02/28/linq-expressions-creating-objects/
        /// </summary>
        /// <param name="dataType">Type of the objectwe wish to create</param>
        /// <returns>Method to return an instance of object</returns>
        public static Func<object[], object> GetActivator(Type dataType)
        {
            var ctor = dataType.GetConstructor(new Type[] { });

            //User has forgotten to include a parameterless constructor:
            if (ctor == null) return null;

            var paramsInfo = ctor.GetParameters();

            //create a single param of type object[]
            var param = Expression.Parameter(typeof(object[]), "args");
            var argsExp = new Expression[paramsInfo.Length];

            for (var i = 0; i < paramsInfo.Length; i++)
            {
                var index = Expression.Constant(i);
                var paramType = paramsInfo[i].ParameterType;
                var paramAccessorExp = Expression.ArrayIndex(param, index);
                var paramCastExp = Expression.Convert(paramAccessorExp, paramType);
                argsExp[i] = paramCastExp;
            }

            var newExp = Expression.New(ctor, argsExp);
            var lambda = Expression.Lambda(typeof(Func<object[], object>), newExp, param);
            return (Func<object[], object>)lambda.Compile();
        }



        /// <summary>
        /// Get a list of all the matching type names in this DLL assembly:
        /// </summary>
        /// <param name="assembly">Assembly dll we're loading.</param>
        /// <param name="baseClassName">Class to instantiate in the library</param>
        /// <returns>String list of types available.</returns>
        public static List<string> GetExtendedTypeNames(Assembly assembly, string baseClassName) {
            var typeNames = new List<string>();
            try
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }


                if (types != null && types.Length > 0)
                {
                    typeNames = (from t in types
                                 where t.IsClass && t.BaseType.Name == baseClassName && t.Name != baseClassName && t.GetConstructor(Type.EmptyTypes) != null
                                 select t.FullName).ToList();
                }
                else
                {
                    Log.Error("API.GetExtendedTypeNames(): No types found in assembly.");
                }
            }
            catch (Exception err)
            {
                Log.Error("API.GetExtendedTypeNames(): " + err.Message + " Inner: " + err.InnerException );
            }
            return typeNames;
        }


        /// <summary>
        /// Get an array of the actual type instances in the user's algorithm
        /// </summary>
        /// <param name="assembly">Assembly dll we're loading.</param>
        /// <param name="baseClassName">Class to instantiate in the library</param>
        /// <returns>String list of types available.</returns>
        public static List<Type> GetExtendedTypes(Assembly assembly, string baseClassName)
        {
            return (from t in assembly.GetTypes()
                    where t.BaseType.Name == baseClassName && t.Name != baseClassName && t.GetConstructor(Type.EmptyTypes) != null
                    select t).ToList();
        }


        /// <summary>
        /// Create a safe application domain with a random name.
        /// </summary>
        /// <param name="appDomainName">Set the name if required</param>
        /// <returns>True on successful creation.</returns>
        private AppDomain CreateAppDomain(string appDomainName = "") {

            //Create new domain name if not supplied:
            if (string.IsNullOrEmpty(appDomainName)) {
                appDomainName = "qclibrary" + Guid.NewGuid().ToString().GetHashCode().ToString("x");
            }

            //Setup the new domain
            AppDomainSetup domainSetup = new AppDomainSetup();

            //Create the domain: set to class variable; return reference.
            appDomain = AppDomain.CreateDomain(appDomainName, null, domainSetup);
            return appDomain;
        }

            

        /// <summary>
        /// Unload this factory's appDomain.
        /// </summary>
        public void Unload() {
            if (this.appDomain != null) {
                AppDomain.Unload(this.appDomain);
                this.appDomain = null;
            }
        }

    } // End Algorithm Factory Class

} // End QC Namespace.
