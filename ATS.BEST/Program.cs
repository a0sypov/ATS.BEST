using ATS.BEST.Services;

using Microsoft.AspNetCore.SignalR;

namespace ATS.BEST
{
    public class KeywordGroups
    {
        public List<string> CoreRequirements { get; set; }
        public List<string> PreferredQualifications { get; set; }
        public List<string> NiceToHave { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"Core Requirements: {string.Join(", ", CoreRequirements)}\n" +
                   $"Preferred Qualifications: {string.Join(", ", PreferredQualifications)}\n" +
                   $"Nice to Have: {string.Join(", ", NiceToHave)}";
        }
    }

    public class CV
    {
        public required string name { get; set; }
        public required string applied_role { get; set; }
        public required Contacts contacts { get; set; }
        public required List<Language> languages { get; set; }
        public required string summary { get; set; }
        public required List<WorkExperience> work_experience { get; set; }
        public required List<Education> education { get; set; }
        public required List<Skill> skills { get; set; }
        public required List<Project> projects { get; set; }
        public required List<string> hobbies { get; set; }
    }


    public abstract class CVSection
    {
        public abstract override string ToString();
    }

    public class Language : CVSection
    {
        public required string name { get; set; }
        public required string level { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"{name} - {level}";
        }
    }

    public class Skill : CVSection
    {
        public required string name { get; set; }
        public required string level { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"{name} - {level}";
        }
    }

    public class Technology : CVSection
    { 
        public required string name { get; set; }

        public override string ToString()
        {
            return name;
        }
    }

    public class Project : CVSection
    {
        public required string name { get; set; }
        public required string description { get; set; }
        public required List<Technology> technologies { get; set; }

        // ToString()
        public override string ToString()
        {
            string allTechnologies = string.Join(", ", technologies.Select(t => t.name));
            return $"Title: {name}:\nUsed technologies: {allTechnologies}\nDescription: {description}\n";
        }
    }

    public class Contacts : CVSection
    {
        public required string phone { get; set; }
        public required string email { get; set; }
        public required List<string> links { get; set; }
        public required string location { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"Phone: {phone}\nEmail: {email}\nLocation: {location}\nLinks: {string.Join(", ", links)}\n";
        }
    }

    public class WorkExperience : CVSection
    {
        public required string role { get; set; }
        public required string company { get; set; }
        public required string dates { get; set; }
        public required List<string> responsibilities { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"Role: {role}:\nCompany: {company}\nDates: {dates}\nResponsibilities: {string.Join("; ", responsibilities)}\n";
        }
    }

    public class Education : CVSection
    {
        public required string title { get; set; }
        public required string institution { get; set; }
        public required string dates { get; set; }
        public required string type { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"Title: {title}:\nType: {type}\nInstitution: {institution}\nDates: {dates}\n";
        }
    }

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
            builder.Services.AddSingleton<OpenAIService>();

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
