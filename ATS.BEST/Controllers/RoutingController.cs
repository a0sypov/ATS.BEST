using ATS.BEST.Services;

using Microsoft.AspNetCore.Mvc;

using UglyToad.PdfPig;

using System.Text.Json;
using static System.Collections.Specialized.BitVector32;
using System.Linq;
using Microsoft.AspNetCore.SignalR;

namespace ATS.BEST.Controllers
{
    public struct ApplicantScore
    {
        public float finalScore { get; set; }
        public float maxScore { get; set; }
        public float workExperienceScore { get; set; }
        public float projectsScore { get; set; }
        public float educationScore { get; set; }
        public float skillsScore { get; set; }
        public float languagesScore { get; set; }

        public ApplicantScore(float _finalScore, float _maxScore, float _workExperienceScore, float _projectsScore, float _educationScore, float _skillsScore, float _languagesScore)
        {
            finalScore = _finalScore;
            maxScore = _maxScore;
            workExperienceScore = _workExperienceScore;
            projectsScore = _projectsScore;
            educationScore = _educationScore;
            skillsScore = _skillsScore;
            languagesScore = _languagesScore;
        }
    }
    public struct Applicant
    {
        public CV CV { get; set; }
        public string AIEvaluation { get; set; }
        public ApplicantScore Scores { get; set; }

        public Applicant(CV _CV)
        {
            CV = _CV;
            AIEvaluation = "";
            Scores = new ApplicantScore();
        }

        public Applicant(CV _CV, ApplicantScore _Score, string _AIEvaluation)
        {
            CV = _CV;
            Scores = new ApplicantScore(_Score.finalScore, _Score.maxScore, _Score.workExperienceScore, _Score.projectsScore, _Score.educationScore, _Score.skillsScore, _Score.languagesScore);
            AIEvaluation = _AIEvaluation;
        }
    }
    public struct ApplicantEvaluation
    {
        public float score { get; set; }
        public string aiEvaluation { get; set; }

        public ApplicantEvaluation(float _score, string _aiEvaluation) : this()
        {
            this.score = _score;
            this.aiEvaluation = _aiEvaluation;
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class RoutingController : Controller
    {
        private readonly OpenAIService _openAi;
        private readonly IHubContext<ProgressHub> _hubContext;

        public RoutingController(OpenAIService openAi, IHubContext<ProgressHub> hubContext)
        {
            _openAi = openAi;
            _hubContext = hubContext;
        }

        [HttpPost]
        [Route("upload")]
        public async Task<IActionResult> ConvertPDFs([FromForm] List<IFormFile> cvs, [FromForm] string jobDescription)
        {
            if (cvs == null || cvs.Count == 0)
                return BadRequest("No files uploaded.");

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Starting...");

            List<Applicant> applicants = new List<Applicant>();

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Prelimenary culling...");
            List<float> jbEmbedding = await _openAi.GetEmbeddingAsync(jobDescription);

            int cvIndex = 1;

            foreach (var cv in cvs)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", $"CVs parsing ({cvIndex}/{cvs.Count})...");
                cvIndex++;
                if (cv.Length > 0)
                {
                    using var stream = cv.OpenReadStream();
                    using var pdf = PdfDocument.Open(stream);

                    var fullText = string.Join("\n", pdf.GetPages().Select(p => p.Text));

                    List<float> cvEmbedding = await _openAi.GetEmbeddingAsync(fullText);

                    double cosineSimilarity = 1.0; // _openAi.CosineSimilarity(jbEmbedding, cvEmbedding);

                    if(cosineSimilarity >= 0.5)
                    {
                        string JSONCV = await _openAi.AI_CVParsing_Async(fullText);
                        string cleanedJSONCV = System.Text.RegularExpressions.Regex.Unescape(JSONCV);

                        CV CV = JsonSerializer.Deserialize<CV>(cleanedJSONCV);
                        applicants.Add(new Applicant(CV));
                    }
                }
            }            

            string workExperiences = string.Join("\n\n", applicants.Select(x => $"{x.CV.name}:\n{string.Join("\n", x.CV.work_experience.Select(x => x.ToString()))}"));
            string educations = string.Join("\n\n", applicants.Select(x => $"{x.CV.name}:\n{string.Join("\n", x.CV.education.Select(x => x.ToString()))}"));
            string projects = string.Join("\n\n", applicants.Select(x => $"{x.CV.name}:\n{string.Join("\n", x.CV.projects.Select(x => x.ToString()))}"));
            string skills = string.Join("\n\n", applicants.Select(x => $"{x.CV.name}:\n{string.Join("\n", x.CV.skills.Select(x => x.ToString()))}"));
            string languages = string.Join("\n\n", applicants.Select(x => $"{x.CV.name}:\n{string.Join("\n", x.CV.languages.Select(x => x.ToString()))}"));

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Work experience evaluation...");
            string workExperienceEval = await _openAi.AI_CVEvaluation_Async("work_experience", workExperiences, jobDescription);
            string[] workExperienceSections = workExperienceEval.Split(new string[] { "-----" }, StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Projects evaluation...");
            string projectsEval = await _openAi.AI_CVEvaluation_Async("projects", projects, jobDescription);
            string[] projectsSections = projectsEval.Split(new string[] { "-----" }, StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Education evaluation...");
            string educationEval = await _openAi.AI_CVEvaluation_Async("education", educations, jobDescription);
            string[] educationSections = educationEval.Split(new string[] { "-----" }, StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Skills evaluation...");
            string skillsEval = await _openAi.AI_CVEvaluation_Async("skills", skills, jobDescription);
            string[] skillsSections = educationEval.Split(new string[] { "-----" }, StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Languages evaluation...");
            string languagesEval = await _openAi.AI_CVEvaluation_Async("languages", languages, jobDescription);
            string[] languagesSections = educationEval.Split(new string[] { "-----" }, StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Parsing AI evaluations");

            Dictionary<string, ApplicantEvaluation> workExperienceRatings = new Dictionary<string, ApplicantEvaluation>();
            string[] workExperienceRatingsList = workExperienceSections.Last().Split('\n', StringSplitOptions.TrimEntries);
            for(int i = workExperienceRatingsList.Length-1; i > workExperienceRatingsList.Length - 1 - applicants.Count; i--)
            {
                string[] nameAndRating = workExperienceRatingsList[i].Split('-');
                string name = nameAndRating[0].Trim();
                float rating = 0;
                Console.WriteLine(nameAndRating.ToString());
                if (float.TryParse(nameAndRating[1].Trim(), out rating))
                {
                    workExperienceRatings.Add(name, new ApplicantEvaluation(rating, workExperienceSections[workExperienceRatingsList.Length - 1 - i]));
                }
            }

            Dictionary<string, ApplicantEvaluation> projectsRatings = new Dictionary<string, ApplicantEvaluation>();
            string[] projectsRatingsList = projectsSections.Last().Split('\n', StringSplitOptions.TrimEntries);
            for (int i = projectsRatingsList.Length - 1; i > projectsRatingsList.Length - 1 - applicants.Count; i--)
            {
                string[] nameAndRating = projectsRatingsList[i].Split('-');
                string name = nameAndRating[0].Trim();
                float rating = 0;
                if (float.TryParse(nameAndRating[1].Trim(), out rating))
                {
                    projectsRatings.Add(name, new ApplicantEvaluation(rating, projectsSections[projectsRatingsList.Length - 1 - i]));
                }
            }

            Dictionary<string, ApplicantEvaluation> educationRatings = new Dictionary<string, ApplicantEvaluation>();
            string[] educationRatingsList = educationSections.Last().Split('\n', StringSplitOptions.TrimEntries);
            for (int i = educationRatingsList.Length - 1; i > educationRatingsList.Length - 1 - applicants.Count; i--)
            {
                string[] nameAndRating = educationRatingsList[i].Split('-');
                string name = nameAndRating[0].Trim();
                float rating = 0;
                if (float.TryParse(nameAndRating[1].Trim(), out rating))
                {
                    educationRatings.Add(name, new ApplicantEvaluation(rating, educationSections[educationRatingsList.Length - 1 - i]));
                }
            }

            Dictionary<string, ApplicantEvaluation> skillsRatings = new Dictionary<string, ApplicantEvaluation>();
            string[] skillsRatingsList = skillsSections.Last().Split('\n', StringSplitOptions.TrimEntries);
            for (int i = skillsRatingsList.Length - 1; i > skillsRatingsList.Length - 1 - applicants.Count; i--)
            {
                string[] nameAndRating = skillsRatingsList[i].Split('-');
                string name = nameAndRating[0].Trim();
                float rating = 0;
                if (float.TryParse(nameAndRating[1].Trim(), out rating))
                {
                    skillsRatings.Add(name, new ApplicantEvaluation(rating, skillsSections[skillsRatingsList.Length - 1 - i]));
                }
            }

            Dictionary<string, ApplicantEvaluation> languagesRatings = new Dictionary<string, ApplicantEvaluation>();
            string[] languagesRatingsList = languagesSections.Last().Split('\n', StringSplitOptions.TrimEntries);
            for (int i = languagesRatingsList.Length - 1; i > languagesRatingsList.Length - 1 - applicants.Count; i--)
            {
                string[] nameAndRating = languagesRatingsList[i].Split('-');
                string name = nameAndRating[0].Trim();
                float rating = 0;
                if (float.TryParse(nameAndRating[1].Trim(), out rating))
                {
                    languagesRatings.Add(name, new ApplicantEvaluation(rating, languagesSections[languagesRatingsList.Length - 1 - i]));
                }
            }

            for (int i = 0; i < applicants.Count; i++)
            {
                CV cv = applicants[i].CV;
                ApplicantScore newScores = new ApplicantScore();
                
                newScores.workExperienceScore = workExperienceRatings[cv.name].score;
                newScores.projectsScore = projectsRatings[cv.name].score;
                newScores.educationScore = educationRatings[cv.name].score;
                newScores.skillsScore = skillsRatings[cv.name].score;
                newScores.languagesScore = languagesRatings[cv.name].score;

                Dictionary<string, float> sectionWeights = new Dictionary<string, float>
                    {
                        { "work_experience", 1.0f },
                        { "projects", 0.7f },
                        { "education", 0.5f },
                        { "skills", 0.3f },
                        { "languages", 0.1f }
                    };

                float finalScore = newScores.workExperienceScore * sectionWeights["work_experience"] +
                                   newScores.projectsScore * sectionWeights["projects"] +
                                   newScores.educationScore * sectionWeights["education"] +
                                   newScores.skillsScore * sectionWeights["skills"] +
                                   newScores.languagesScore * sectionWeights["languages"];
                newScores.finalScore = finalScore;

                float maxScore = 10 * sectionWeights["work_experience"] +
                                   10 * sectionWeights["projects"] +
                                   10 * sectionWeights["education"] +
                                   10 * sectionWeights["skills"] +
                                   10 * sectionWeights["languages"];
                newScores.maxScore = maxScore;

                string AIEvaluation = $"Work experience:\n{workExperienceRatings[cv.name].aiEvaluation}\n\nProjects:\n{projectsRatings[cv.name].aiEvaluation}\n\nEducation:\n{educationRatings[cv.name].aiEvaluation}\n\nSkills:\n{skillsRatings[cv.name].aiEvaluation}\n\nLanguages:\n{languagesRatings[cv.name].aiEvaluation}";
                applicants[i] = new Applicant(cv, newScores, AIEvaluation);
            }
            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Done!");


            /*
            for (int i = 0; i < applicants.Count; i++)
            {
                CV cv = applicants[i].CV;
                ApplicantScore newScores = new ApplicantScore();

                List<CVSection> workExperienceSections = cv.work_experience.Cast<CVSection>().ToList();
                string workExperienceEval = await _openAi.AI_CVEvaluation_Async("work_experience", workExperienceSections, jobDescription);

                float workExperienceScore = ParseEvaluationScore(workExperienceEval);
                newScores.workExperienceScore = workExperienceScore;

                List<CVSection> projectsSections = cv.projects.Cast<CVSection>().ToList();
                string projectsEval = await _openAi.AI_CVEvaluation_Async("projects", projectsSections, jobDescription);
                float projectsScore = ParseEvaluationScore(projectsEval);
                newScores.projectsScore = projectsScore;

                List<CVSection> educationSections = cv.education.Cast<CVSection>().ToList();
                string educationEval = await _openAi.AI_CVEvaluation_Async("education", educationSections, jobDescription);
                float educationScore = ParseEvaluationScore(educationEval);
                newScores.educationScore = educationScore;

                List<CVSection> skillsSections = cv.skills.Cast<CVSection>().ToList();
                string skillsEval = await _openAi.AI_CVEvaluation_Async("skills", skillsSections, jobDescription);
                float skillsScore = ParseEvaluationScore(skillsEval);
                newScores.skillsScore = skillsScore;

                List<CVSection> languagesSections = cv.languages.Cast<CVSection>().ToList();
                string languagesEval = await _openAi.AI_CVEvaluation_Async("languages", languagesSections, jobDescription);
                float languagesScore = ParseEvaluationScore(languagesEval);
                newScores.languagesScore = languagesScore;

                // Dictionary of section weights
                Dictionary<string, float> sectionWeights = new Dictionary<string, float>
                {
                    { "work_experience", 1.0f },
                    { "projects", 0.7f },
                    { "education", 0.5f },
                    { "skills", 0.3f },
                    { "languages", 0.1f }
                };

                float finalScore = workExperienceScore * sectionWeights["work_experience"] +
                                   projectsScore * sectionWeights["projects"] +
                                   educationScore * sectionWeights["education"] +
                                   skillsScore * sectionWeights["skills"] +
                                   languagesScore * sectionWeights["languages"];
                newScores.finalScore = finalScore;

                float maxScore = 5 * sectionWeights["work_experience"] +
                                   5 * sectionWeights["projects"] +
                                   5 * sectionWeights["education"] +
                                   5 * sectionWeights["skills"] +
                                   5 * sectionWeights["languages"];
                newScores.maxScore = maxScore;

                // Print of all scores with a name of a person
                Console.WriteLine($"Name: {cv.name}, Work Experience Score: {workExperienceScore}, Projects Score: {projectsScore}, Education Score: {educationScore}, Skills Score: {skillsScore}, Languages Score: {languagesScore}, Final Score: {finalScore}");

                string AIEvaluation = $"Work experience:\n{workExperienceEval}\n\nProjects:\n{projectsEval}\n\nEducation:\n{educationEval}\n\nSkills:\n{skillsEval}\n\nLanguages:\n{languagesEval}";
                applicants[i] = new Applicant(cv, newScores, AIEvaluation);
            }*/

            await Task.CompletedTask;
            return Ok( applicants );
        }

        private float ParseEvaluationScore(string evaluation)
        {
            float score = 0;
            if (float.TryParse(evaluation.Last().ToString(), out score))
            {
                // Successfully parsed last element
            }
            else if (float.TryParse(evaluation[evaluation.Length - 2].ToString(), out score))
            {
                // Successfully parsed second to last element
            }
            return score;
        }
    }
}
