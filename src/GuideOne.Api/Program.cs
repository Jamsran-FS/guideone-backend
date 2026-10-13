using GuideOne.Api.Auth;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Scalar.AspNetCore;
using Supabase.Extensions.DependencyInjection;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SupabaseExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();

builder.Services.AddSupabase(
    builder.Configuration["Supabase:Url"]!,
    builder.Configuration["Supabase:Key"]!
);

var supabase = builder.Configuration.GetSection("Supabase").Get<SupabaseOptions>()!;
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection("Supabase"));
builder.Services.AddHttpClient<SupabaseRest>(http =>
{
    http.BaseAddress = new Uri(supabase.Url.TrimEnd('/') + "/");
    http.DefaultRequestHeaders.Add("apikey", supabase.Key);
});

// Supabase Auth-ийн JWT-г (ES256, JWKS) шалгана. Нэвтрэх/бүртгүүлэх нь Supabase дээр явагдана.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{supabase.Url.TrimEnd('/')}/auth/v1";
        options.TokenValidationParameters.ValidAudience = "authenticated";
        options.MapInboundClaims = false;
        options.SaveToken = true;
    });
builder.Services.AddScoped<ProfileRoles>();
builder.Services.AddScoped<IAuthorizationHandler, RoleHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Sysadmin, policy => policy.RequireAuthenticatedUser().AddRequirements(new RoleRequirement(Roles.Sysadmin)))
    .AddPolicy(Policies.Teacher, policy => policy.RequireAuthenticatedUser().AddRequirements(new RoleRequirement(Roles.Teacher)));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
