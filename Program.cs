using Azure.Data.Tables;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ML.Charity.API.Client.Models;
using ML.Charity.API.Client.Services;
using System.Text;
using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------
// 1. Controllers & Swagger Configuration
// -----------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger to support JWT Bearer authorization testing
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ibada Kit Challenge API",
        Version = "v1"
    });

    // Add JWT Bearer definition to Swagger UI
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token directly (without the word 'Bearer')."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// -----------------------------------------------------------
// 2. Azure Table Storage Registration
// -----------------------------------------------------------
var storageConnectionString = builder.Configuration.GetConnectionString("AzureTableStorage");
if (string.IsNullOrWhiteSpace(storageConnectionString))
{
    storageConnectionString = builder.Configuration["AzureTableStorage"]
        ?? Environment.GetEnvironmentVariable("AzureTableStorage")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__AzureTableStorage")
        ?? Environment.GetEnvironmentVariable("CUSTOMCONNSTR_AzureTableStorage");
}

if (string.IsNullOrWhiteSpace(storageConnectionString))
{
    throw new InvalidOperationException("AzureTableStorage connection string is missing. Please configure 'AzureTableStorage' or 'ConnectionStrings:AzureTableStorage' in environment variables.");
}

builder.Services.AddSingleton(new TableServiceClient(storageConnectionString));

// Register repositories for each Table Entity
builder.Services.AddSingleton<ITableStorageService<DonationEntity>>(sp =>
{
    var client = sp.GetRequiredService<TableServiceClient>();
    return new TableStorageService<DonationEntity>(client, "Donations");
});

builder.Services.AddSingleton<ITableStorageService<UserEntity>>(sp =>
{
    var client = sp.GetRequiredService<TableServiceClient>();
    return new TableStorageService<UserEntity>(client, "Users");
});

builder.Services.AddSingleton<ITableStorageService<OtpSessionEntity>>(sp =>
{
    var client = sp.GetRequiredService<TableServiceClient>();
    return new TableStorageService<OtpSessionEntity>(client, "OtpSessions");
});

builder.Services.AddSingleton<ITableStorageService<SponsorshipItemEntity>>(sp =>
{
    var client = sp.GetRequiredService<TableServiceClient>();
    return new TableStorageService<SponsorshipItemEntity>(client, "SponsorshipItems");
});

builder.Services.AddSingleton<ITableStorageService<SponsorshipEntity>>(sp =>
{
    var client = sp.GetRequiredService<TableServiceClient>();
    return new TableStorageService<SponsorshipEntity>(client, "Sponsorships");
});

builder.Services.AddSingleton<ITableStorageService<WardEntity>>(sp =>
{
    var client = sp.GetRequiredService<TableServiceClient>();
    return new TableStorageService<WardEntity>(client, "Wards");
});

builder.Services.AddSingleton<ITableStorageService<CampaignSettingsEntity>>(sp =>
{
    var client = sp.GetRequiredService<TableServiceClient>();
    return new TableStorageService<CampaignSettingsEntity>(client, "CampaignSettings");
});

// -----------------------------------------------------------
// 3. Application Services
// -----------------------------------------------------------
builder.Services.AddScoped<JwtService>();
builder.Services.AddHttpClient<IWhatsAppService, MetaWhatsAppService>();
// -----------------------------------------------------------
// 4. JWT Authentication & Bearer Validation Configuration
// -----------------------------------------------------------
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(secretKey))
{
    secretKey = builder.Configuration["Jwt__Key"]
        ?? builder.Configuration["JWT_SECRET_KEY"]
        ?? builder.Configuration["JWT_KEY"]
        ?? Environment.GetEnvironmentVariable("Jwt__Key")
        ?? Environment.GetEnvironmentVariable("Jwt:Key")
        ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
        ?? Environment.GetEnvironmentVariable("JWT_KEY");
}

if (string.IsNullOrWhiteSpace(secretKey))
{
    throw new InvalidOperationException("JWT Secret Key is missing in configuration. Ensure 'Jwt:Key' or 'JWT_SECRET_KEY' is configured in environment variables.");
}
var keyBytes = Encoding.UTF8.GetBytes(secretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Set to true in strict production environment
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero // Eliminates the default 5-minute clock drift allowance
    };

    // Custom response on unauthorized failure (optional, useful for debugging)
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is SecurityTokenExpiredException)
            {
                context.Response.Headers.Append("Token-Expired", "true");
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Configure CORS for web client access
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddBlobServiceClient(builder.Configuration["ConnectionStrings:AzureTableStorage:blobServiceUri"]!).WithName("ConnectionStrings:AzureTableStorage");
    clientBuilder.AddQueueServiceClient(builder.Configuration["ConnectionStrings:AzureTableStorage:queueServiceUri"]!).WithName("ConnectionStrings:AzureTableStorage");
    clientBuilder.AddTableServiceClient(builder.Configuration["ConnectionStrings:AzureTableStorage:tableServiceUri"]!).WithName("ConnectionStrings:AzureTableStorage");
});

var app = builder.Build();

// -----------------------------------------------------------
// 5. HTTP Request Pipeline & Middleware
// -----------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors();

// CRITICAL: Authentication MUST be placed before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();