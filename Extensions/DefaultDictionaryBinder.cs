using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.ComponentModel;

    /// <summary>
    /// ASP.NET MVC Default Dictionary Binder
    /// </summary>
    public class DefaultDictionaryBinder : DefaultModelBinder
    {
        IModelBinder nextBinder;

        /// <summary>
        /// Create an instance of DefaultDictionaryBinder.
        /// </summary>
        public DefaultDictionaryBinder() : this(null)
        {
        }

        /// <summary>
        /// Create an instance of DefaultDictionaryBinder.
        /// </summary>
        /// <param name="nextBinder">The next model binder to chain call. If null, by default, the DefaultModelBinder is called.</param>
        public DefaultDictionaryBinder(IModelBinder nextBinder)
        {
            this.nextBinder = nextBinder;
        }

        private IEnumerable<string> GetValueProviderKeys(ControllerContext context)
        {
#if !ASPNETMVC1
            IDictionary contextItems = HttpContext.Current.Items;
            // Do not reference this key elsewhere, this is strictly for cache.
            if (!contextItems.Contains("ValueProviderKeys"))
            {
                List<string> keys = new List<string>();
                keys.AddRange(context.HttpContext.Request.Form.Keys.Cast<string>());
                keys.AddRange(((IDictionary<string, object>)context.RouteData.Values).Keys.Cast<string>());
                keys.AddRange(context.HttpContext.Request.QueryString.Keys.Cast<string>());
                keys.AddRange(context.HttpContext.Request.Files.Keys.Cast<string>());
                contextItems["ValueProviderKeys"] = keys;
            }
            return (IEnumerable<string>)contextItems["ValueProviderKeys"];
#else
            return bindingContext.ValueProvider.Keys;
#endif
        }

        private object ConvertType(string stringValue, Type type)
        {
            return TypeDescriptor.GetConverter(type).ConvertFrom(stringValue);
        }

        public override object BindModel(ControllerContext controllerContext, ModelBindingContext bindingContext)
        {
            Type modelType = bindingContext.ModelType;
            Type idictType = null;
            Type genericTypeDefinition = null;

            // For collection classes, proceed as dictionary, then convert back to list.
            if (modelType.IsGenericType) {
                genericTypeDefinition = modelType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(IDictionary<,>)) {
                    idictType = typeof(Dictionary<,>).MakeGenericType(modelType.GetGenericArguments());
                }
                else if (genericTypeDefinition == typeof(IEnumerable<>) || genericTypeDefinition == typeof(ICollection<>) || genericTypeDefinition == typeof(IList<>)) {
                    Type[] genericItemType = modelType.GetGenericArguments();
                    Type[] genericArgs = new Type[] { typeof(int),  genericItemType[0] };
                    idictType = typeof(Dictionary<,>).MakeGenericType(genericArgs);
                }
            }

            if (idictType != null)
            {
                object result = null;

                Type[] ga = idictType.GetGenericArguments();
                IModelBinder valueBinder = Binders.GetBinder(ga[1]);
                List<object> dictionaryKeys = new List<object>();
                
                foreach (string key in GetValueProviderKeys(controllerContext))
                {
                    if (key.StartsWith(bindingContext.ModelName + "[", StringComparison.InvariantCultureIgnoreCase))
                    {
                        int endbracket = key.IndexOf("]", bindingContext.ModelName.Length + 1);
                        if (endbracket == -1)
                            continue;

                        object dictKey;
                        try
                        {
                            dictKey = ConvertType(key.Substring(bindingContext.ModelName.Length + 1, endbracket - bindingContext.ModelName.Length - 1), ga[0]);
                        }
                        catch (NotSupportedException)
                        {
                            continue;
                        }

                        if (dictionaryKeys.Contains(dictKey))
                        {
                            continue;
                        }
                        dictionaryKeys.Add(dictKey);

                        ModelBindingContext innerBindingContext = new ModelBindingContext()
                        {
#if ASPNETMVC1
                            Model = null,
                            ModelType = ga[1],
#else
                            ModelMetadata = ModelMetadataProviders.Current.GetMetadataForType(() => null, ga[1]),
#endif
                            ModelName = key.Substring(0, endbracket + 1),
                            ModelState = bindingContext.ModelState,
                            PropertyFilter = bindingContext.PropertyFilter,
                            ValueProvider = bindingContext.ValueProvider
                        };
                        object newPropertyValue = valueBinder.BindModel(controllerContext, innerBindingContext);

                        if (result == null)
                            result = CreateModel(controllerContext, bindingContext, idictType);

                        if (!(bool)idictType.GetMethod("ContainsKey").Invoke(result, new object[] { dictKey }))
                            idictType.GetProperty("Item").SetValue(result, newPropertyValue, new object[] { dictKey });
                    }
                }

                if (result != null)
                {
                    if (genericTypeDefinition == typeof(IEnumerable<>) || genericTypeDefinition == typeof(ICollection<>) || genericTypeDefinition == typeof(IList<>))
                    {
                        //  Here is where we convert back to a list.
                        IEnumerable collectionResult = (IEnumerable)idictType.GetProperty("Values").GetValue(result, null);
                        Type listType = typeof(List<>).MakeGenericType(modelType.GetGenericArguments());
                        object listObject = Activator.CreateInstance(listType);
                        foreach (var item in collectionResult)
                        {
                            listType.GetMethod("Add").Invoke(listObject, new object[] { item });
                        }

                        return listObject;
                    }
                }

                return result;
            }

            if (nextBinder != null)
            {
                return nextBinder.BindModel(controllerContext, bindingContext);
            }

            return base.BindModel(controllerContext, bindingContext);
        }
    }

