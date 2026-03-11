using Asp.Versioning.ApiExplorer;
using DiyanetNamazVakti.Api.WebCommon.ActionFilters;
using DiyanetNamazVakti.Api.WebCommon.Extensions;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options => options.AddDefaultPolicy(builder => builder.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

// Rate Limiting → 50 Anfragen pro Minute pro API Key
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("perApiKey", context =>
    {
        var apiKey = context.Request.Headers["X-API-Key"].ToString();
        var key = string.IsNullOrEmpty(apiKey) ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown" : apiKey;

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 50,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"statusCode\":429,\"message\":\"Zu viele Anfragen. Bitte warte eine Minute.\"}",
            token);
    };
});

// AwqatSalah Service Settings
var awqatSalahSettings = builder.Configuration.GetSection(nameof(AwqatSalahSettings));
builder.Services.Configure<AwqatSalahSettings>(awqatSalahSettings);
builder.Services.AddSingleton<IAwqatSalahSettings>(sp => sp.GetRequiredService<IOptions<AwqatSalahSettings>>().Value);
builder.Services.AddHttpClient("AwqatSalahApi", client => { client.BaseAddress = new Uri(awqatSalahSettings.Get<AwqatSalahSettings>()!.ApiUri); });

//This api settings
builder.Services.Configure<MyApiClientSettings>(builder.Configuration.GetSection(nameof(MyApiClientSettings)));
builder.Services.AddSingleton<IMyApiClientSettings>(sp => sp.GetRequiredService<IOptions<MyApiClientSettings>>().Value);

// CacheSettings
builder.Services.Configure<CacheSettings>(builder.Configuration.GetSection(nameof(CacheSettings)));
builder.Services.AddSingleton<ICacheSettings>(sp => sp.GetRequiredService<IOptions<CacheSettings>>().Value);
builder.Services.AddSingleton<IMemoryCache, MemoryCache>();
builder.Services.AddSingleton<ICacheService, TursoCacheService>();

//Api Call Service Dependence
builder.Services.AddScoped<IAwqatSalahConnectService, AwqatSalahApiService>();

//Service Dependencies
builder.Services.AddTransient<IPlaceService, PlaceService>();
builder.Services.AddTransient<IDailyContentService, DailyContentService>();
builder.Services.AddTransient<IAwqatSalahService, AwqatSalahService>();

builder.Services
    .AddControllers(opt => opt.Filters.Add<ClientAtionFilter>())
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Where(e => e.Value.Errors.Count > 0).Select(e => new ValidationErrorModel
            {
                Name = e.Key,
                Message = e.Value.Errors.First().ErrorMessage
            }).ToList();
            throw new ValidationException(JsonSerializer.Serialize<List<ValidationErrorModel>>(errors));
        };
    });

//Api Versioning
builder.Services.AddAndConfigureApiVersioning();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureSwagger();

var app = builder.Build();

// Configure the HTTP request pipeline.
var apiVersionDescriptionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
app.UseSwagger(apiVersionDescriptionProvider);
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("perApiKey");
app.Run();
