using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Webboard.Domain.Interfaces.Repositories;
using Webboard.Domain.Interfaces.Services;
using Webboard.Domain.Services;
using Webboard.Infrastructure.Configuration;
using Webboard.Infrastructure.Repositories;

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

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
       .AddCookie(configureOptions: options => {
           options.LoginPath = "/Users";
           options.AccessDeniedPath = "/Users";
           options.ExpireTimeSpan = TimeSpan.FromHours(hours: 8);
           options.SlidingExpiration = true;
       });
builder.Services.AddAuthorization();

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
