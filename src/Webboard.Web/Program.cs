using DotNetEnv;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Webboard.Domain.Interfaces.Repositories;
using Webboard.Domain.Interfaces.Services;
using Webboard.Domain.Services;
using Webboard.Infrastructure.Configuration;
using Webboard.Infrastructure.Repositories;
using Webboard.Web.Authentication;

Env.NoClobber()
   .TraversePath()
   .Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<WebboardDbContext>(optionsAction: options =>
    options.UseNpgsql(
    builder.Configuration.GetConnectionString("WebboardDatabase")
    ?? throw new InvalidOperationException(
    "Connection string 'WebboardDatabase' is not configured.")));

builder.Services.AddScoped<IUserCrudAccessRepository, UserCrudAccessRepository>();
builder.Services.AddScoped<IUserCrudAccessService, UserCrudAccessService>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IUserAuthenticationRepository, UserAuthenticationRepository>();
builder.Services.AddScoped<IHostRepository, HostRepository>();
builder.Services.AddScoped<IHostManagementService, HostManagementService>();
builder.Services.AddScoped<IModuleRepository, ModuleRepository>();
builder.Services.AddScoped<IModuleManagementService, ModuleManagementService>();
builder.Services.AddScoped<JwtSessionService>();
builder.Services
    .AddHttpClient<IAgentHealthChecker, Webboard.Web.Services.AgentHealthChecker>(client =>
        client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });
builder.Services
    .AddHttpClient<IDockerAgentClient, Webboard.Web.Services.DockerAgentClient>(client =>
        client.Timeout = TimeSpan.FromSeconds(5));
builder.Services
    .AddHttpClient<IModuleHealthChecker, Webboard.Web.Services.HttpModuleHealthChecker>(client =>
        client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
    throw new InvalidOperationException(
        "Authentication:Jwt:SigningKey must be configured with at least 32 bytes.");
if (jwtOptions.LifetimeHours <= 0 || jwtOptions.RememberMeDays <= 0)
    throw new InvalidOperationException(
        "JWT lifetime settings must be greater than zero.");

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context => {
                context.Token = context.Request.Cookies[JwtOptions.CookieName];
                return Task.CompletedTask;
            },
            OnChallenge = context => {
                if (!context.Response.HasStarted &&
                    HttpMethods.IsGet(context.Request.Method)) {
                    context.HandleResponse();
                    var returnUrl = context.Request.PathBase + context.Request.Path +
                                    context.Request.QueryString;
                    context.Response.Redirect(
                        $"/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}");
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy(HostAccess.Read, policy =>
        policy.RequireClaim(JwtSessionService.AccessClaimType, HostAccess.Read)));

builder.Services.AddRazorPages();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment()){
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
