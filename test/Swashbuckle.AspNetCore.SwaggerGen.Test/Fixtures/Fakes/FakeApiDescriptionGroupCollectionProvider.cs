﻿#define FORK_V2

using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Buffers;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Newtonsoft.Json;

#if NETCOREAPP3_0
using Microsoft.AspNetCore.Mvc.Infrastructure;
#if FORK_V2
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.AspNetCore.Mvc.DataAnnotations.Internal;
#endif
#else
using Microsoft.AspNetCore.Mvc.DataAnnotations.Internal;
using Microsoft.AspNetCore.Mvc.Internal;
#endif

namespace Swashbuckle.AspNetCore.SwaggerGen.Test
{
    public class FakeApiDescriptionGroupCollectionProvider : IApiDescriptionGroupCollectionProvider
    {
        private readonly List<ControllerActionDescriptor> _actionDescriptors;
        private ApiDescriptionGroupCollection _apiDescriptionGroupCollection;

        public FakeApiDescriptionGroupCollectionProvider()
        {
            _actionDescriptors = new List<ControllerActionDescriptor>();
        }

        public FakeApiDescriptionGroupCollectionProvider Add(
            string httpMethod,
            string routeTemplate,
            string actionName,
            Type controllerType = null)
        {
            controllerType = controllerType ?? typeof(FakeController);
            _actionDescriptors.Add(CreateActionDescriptor(httpMethod, routeTemplate, controllerType, actionName));
            return this;
        }

        public ApiDescriptionGroupCollection ApiDescriptionGroups
        {
            get
            {
                if (_apiDescriptionGroupCollection == null)
                {
                    var apiDescriptions = GetApiDescriptions();
                    var group = new ApiDescriptionGroup("default", apiDescriptions);
                    _apiDescriptionGroupCollection = new ApiDescriptionGroupCollection(new[] { group }, 1);
                }

                return _apiDescriptionGroupCollection;
            }
        }

        private ControllerActionDescriptor CreateActionDescriptor(
            string httpMethod,
            string routeTemplate,
            Type controllerType,
            string actionName)
        {
            var descriptor = new ControllerActionDescriptor();

#if NETCOREAPP3_0
            //see https://github.com/domaindrivendev/Swashbuckle.AspNetCore/pull/1316#discussion_r334642335
            var type = typeof(ControllerBase).Assembly.GetType("Microsoft.AspNetCore.Mvc.ApiDescriptionActionData", throwOnError: true);
            var instance = Activator.CreateInstance(type);
            descriptor.Properties[type] = instance;
#else
            descriptor.SetProperty(new ApiDescriptionActionData());
#endif

            descriptor.ActionConstraints = new List<IActionConstraintMetadata>();
            if (httpMethod != null)
                descriptor.ActionConstraints.Add(new HttpMethodActionConstraint(new[] { httpMethod }));

            descriptor.MethodInfo = controllerType.GetMethod(actionName);
            if (descriptor.MethodInfo == null)
                throw new InvalidOperationException(
                    string.Format("{0} is not declared in {1}", actionName, controllerType));

            descriptor.Parameters = new List<ParameterDescriptor>();
            foreach (var parameterInfo in descriptor.MethodInfo.GetParameters())
            {
                descriptor.Parameters.Add(new ControllerParameterDescriptor
                {
                    Name = parameterInfo.Name,
                    ParameterType = parameterInfo.ParameterType,
                    ParameterInfo = parameterInfo,
                    BindingInfo = BindingInfo.GetBindingInfo(parameterInfo.GetCustomAttributes(false))
                });
            };

            descriptor.ControllerTypeInfo = controllerType.GetTypeInfo();

            descriptor.FilterDescriptors = descriptor.MethodInfo.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select((filter) => new Microsoft.AspNetCore.Mvc.Filters.FilterDescriptor(filter, FilterScope.Action))
                .ToList();

            descriptor.RouteValues = new Dictionary<string, string> {
                { "controller", controllerType.Name.Replace("Controller", string.Empty) }
            };

            var httpMethodAttribute = descriptor.MethodInfo.GetCustomAttributes()
                .OfType<HttpMethodAttribute>()
                .FirstOrDefault();

            descriptor.AttributeRouteInfo = new AttributeRouteInfo
            {
                Template = httpMethodAttribute?.Template ?? routeTemplate,
                Name = httpMethodAttribute?.Name
            };

            return descriptor;
        }

        private IReadOnlyList<ApiDescription> GetApiDescriptions()
        {
            var context = new ApiDescriptionProviderContext(_actionDescriptors);
            var options = new MvcOptions();

#if NETCOREAPP3_0
            var inputFormatter = new NewtonsoftJsonInputFormatter(Mock.Of<ILogger>(), new JsonSerializerSettings(), ArrayPool<char>.Shared, new DefaultObjectPoolProvider(), options, new MvcNewtonsoftJsonOptions());
            var outputFormatter = new NewtonsoftJsonOutputFormatter(new JsonSerializerSettings(), ArrayPool<char>.Shared, options);
#else
            var inputFormatter = new JsonInputFormatter(Mock.Of<ILogger>(), new JsonSerializerSettings(), ArrayPool<char>.Shared, new DefaultObjectPoolProvider());
            var outputFormatter = new JsonOutputFormatter(new JsonSerializerSettings(), ArrayPool<char>.Shared);
#endif
            options.InputFormatters.Add(inputFormatter);
            options.OutputFormatters.Add(outputFormatter);

            var constraintResolver = new Mock<IInlineConstraintResolver>();
            constraintResolver.Setup(i => i.ResolveConstraint("int")).Returns(new IntRouteConstraint());

#if NETCOREAPP3_0
            var provider = new DefaultApiDescriptionProvider(
                Options.Create(options),
                constraintResolver.Object,
                CreateModelMetadataProvider(),
                new Mock<IActionResultTypeMapper>().Object,
                Options.Create(new RouteOptions())
            );
#else
            var provider = new DefaultApiDescriptionProvider(
                Options.Create(options),
                constraintResolver.Object,
                CreateModelMetadataProvider()
            );
#endif

            provider.OnProvidersExecuting(context);
            provider.OnProvidersExecuted(context);
            return new ReadOnlyCollection<ApiDescription>(context.Results);
        }

        public IModelMetadataProvider CreateModelMetadataProvider()
        {
#if NETCOREAPP3_0
#if EMPTY
            return new EmptyModelMetadataProvider();
#elif FORK_V2
            var detailsProviders = new IMetadataDetailsProvider[]
            {
                new FORK_V2_DefaultBindingMetadataProvider(),
                new FORK_V2_DefaultValidationMetadataProvider(),
                new FORK_V2_DataAnnotationsMetadataProvider(
                    Options.Create(new MvcDataAnnotationsLocalizationOptions()),
                    null),
                new BindingSourceMetadataProvider(typeof(CancellationToken), BindingSource.Special),
                new BindingSourceMetadataProvider(typeof(IFormFile), BindingSource.FormFile),
                new BindingSourceMetadataProvider(typeof(IFormFileCollection), BindingSource.FormFile),
                new BindingSourceMetadataProvider(typeof(IEnumerable<IFormFile>), BindingSource.FormFile)
            };

            var compositeDetailsProvider = new FORK_V2_DefaultCompositeMetadataDetailsProvider(detailsProviders);
            return new DefaultModelMetadataProvider(compositeDetailsProvider, Options.Create(new MvcOptions()));
#elif FORK_V3
            var detailsProviders = new IMetadataDetailsProvider[]
            {
                new FORK_V3_DefaultBindingMetadataProvider(),
                new FORK_V3_DefaultValidationMetadataProvider(),
                new FORK_V3_DataAnnotationsMetadataProvider(
                    new MvcOptions(),
                    Options.Create(new MvcDataAnnotationsLocalizationOptions()),
                    null),
                new BindingSourceMetadataProvider(typeof(CancellationToken), BindingSource.Special),
                new BindingSourceMetadataProvider(typeof(IFormFile), BindingSource.FormFile),
                new BindingSourceMetadataProvider(typeof(IFormFileCollection), BindingSource.FormFile),
                new BindingSourceMetadataProvider(typeof(IEnumerable<IFormFile>), BindingSource.FormFile)
            };

            var compositeDetailsProvider = new FORK_V3_DefaultCompositeMetadataDetailsProvider(detailsProviders);
            return new DefaultModelMetadataProvider(compositeDetailsProvider, Options.Create(new MvcOptions()));
#endif
#else
            var detailsProviders = new IMetadataDetailsProvider[]
            {
                new DefaultBindingMetadataProvider(),
                new DefaultValidationMetadataProvider(),
                new DataAnnotationsMetadataProvider(
                    Options.Create(new MvcDataAnnotationsLocalizationOptions()),
                    null),
                new BindingSourceMetadataProvider(typeof(CancellationToken), BindingSource.Special),
                new BindingSourceMetadataProvider(typeof(IFormFile), BindingSource.FormFile),
                new BindingSourceMetadataProvider(typeof(IFormFileCollection), BindingSource.FormFile),
                new BindingSourceMetadataProvider(typeof(IEnumerable<IFormFile>), BindingSource.FormFile)
            };

            var compositeDetailsProvider = new DefaultCompositeMetadataDetailsProvider(detailsProviders);
            return new DefaultModelMetadataProvider(compositeDetailsProvider, Options.Create(new MvcOptions()));
#endif
        }
    }
}