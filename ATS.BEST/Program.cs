using ATS.BEST.Services;

using Microsoft.AspNetCore.SignalR;

namespace ATS.BEST
{
    

    public class ProgressHub : Hub
    {
        public async Task SendProgress(string message, int percentage)
        {
            await Clients.All.SendAsync("ReceiveProgress", message, percentage);
        }
    }


    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.AddHttpClient(); // for HttpClientFactory
            builder.Services.AddScoped<OpenAIService>();

            builder.Services.AddSignalR();

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins("https://atsbest-app-20250407100350.braveglacier-1ed5cedb.westeurope.azurecontainerapps.io/") // your frontend port here
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseCors(); // Enable the CORS middleware

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            app.MapHub<ProgressHub>("/progressHub");
            app.MapControllers();

            app.Run();
        }
    }
}
