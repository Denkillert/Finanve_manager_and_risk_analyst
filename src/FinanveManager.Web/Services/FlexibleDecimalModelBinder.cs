using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FinanveManager.Web.Services;

public sealed class FlexibleDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var modelType = context.Metadata.UnderlyingOrModelType;
        if (modelType == typeof(decimal))
            return new FlexibleDecimalModelBinder();
        return null;
    }
}

public sealed class FlexibleDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);
        var raw = valueProviderResult.FirstValue;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (bindingContext.ModelMetadata.IsNullableValueType)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            return Task.CompletedTask;
        }

        string normalized = raw.Trim()
            .Replace("\u00A0", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(',', '.');

        if (decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"Некорректное числовое значение «{raw}».");
        }

        return Task.CompletedTask;
    }
}
