using System.Text;
using System.Text.Json;

namespace ATS.BEST.Services
{
    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public OpenAIService(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClient = httpClientFactory.CreateClient();
            _apiKey = config["OpenAI:ApiKey"]; // Store your key in appsettings.json or secrets
        }

        public async Task<string> AI_CVParsing_Async(string inputText)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { 
                        role = "system", 
                        content =
                        """
                        You are an AI assistant designed to extract structured information from resumes or CVs. Your task is to read the provided resume text and output the data in the following JSON format. If certain fields are missing or cannot be found, use null or an empty array as appropriate. Parse the CV text exactly as provided, without modifications, summaries, or opinions. DO NOT answer or write anything, but json output.              

                        Please return the data in the following structure:
                        {
                          "name": "Full name of the candidate",
                          "applied_role": "Role or job title the person is applying for",
                          "contacts": {
                            "phone": "Phone number",
                            "email": "Email address",
                            "links": ["LinkedIn, GitHub, portfolio, etc."],
                            "location": "City, region, or address if specified"
                          },
                          "languages": [
                            {
                                "name" : "Language name",
                                "level" : "Proficiency level (e.g., fluent, intermediate, basic, C1, B2, A1, not specified etc.)"
                            }],
                          "summary": "Brief personal summary or career overview",
                          "work_experience": [
                            {
                              "role": "Job title",
                              "company": "Employer name",
                              "dates": "Start - End or time period",
                              "responsibilities": ["List of duties and accomplishments"]
                            }
                          ],
                          "education": [
                            {
                              "title": "Degree or certificate name",
                              "institution": "School or organization name",
                              "dates": "Start - End or year",
                              "type": "diploma or certification"
                            }
                          ],
                          "skills": [
                           {
                              "name" : "Skill name",
                              "level" : "Proficiency level (e.g., expert, intermediate, basic, not specified, etc.)"
                           }
                          ],
                          "projects": [
                            {
                              "name": "Project name",
                              "description": "Short explanation of the project",
                              "technologies": [
                                {
                                    "name" : "Technology name"
                                }
                              ]
                            }
                          ],
                          "hobbies": ["Personal interests or activities"]
                        }
                        """
                    },
                    
                    new { 
                        role = "user", 
                        content = inputText 
                    }
                },
                temperature = 0.7
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error: {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        public async Task<string> AI_CVEvaluation_Async(string category, List<CVSection> cvSection, string jobDescription)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            string intro = "You are a recruitment evaluation assistant. Your task is to rate how well a candidate’s qualifications match a job description. You will receive a specific part of the candidate’s CV and the full job description. Rate the match on a scale from 1 to 5 based on relevance, completeness, and depth.\n";
            string outro = "\nBriefly explain your reasoning. At the very end (in the last line), provide a rating from 1 (not relevant) to 5 (highly aligned) without anything after it (format : FINAL RATING - n). The last symbol of your response should be a rating.";
            string main = "";
            switch(category)
            {
                case "work_experience":
                    main = """
                        Given the candidate’s work experience and the job description, rate how well their professional history aligns with the job requirements.

                        Consider:
                            - Relevant industries and job titles
                            - Seniority level
                            - Duration and depth of experience
                            - Mentioned responsibilities or achievements
                        """;
                    break;
                case "projects":
                    main = """
                        Evaluate the relevance of the candidate’s projects in relation to the job description.

                        Consider:
                            - Relevance of project domains or goals
                            - Technologies used in projects
                            - Complexity or scale
                            - Individual contributions
                        """;
                    break;
                case "education":
                    main = """
                        Assess how closely the candidate’s education and certifications match the expectations in the job description.

                        Consider:
                            - Required degrees or fields of study
                            - Relevance of additional certifications
                            - Prestige or relevance of institutions (if applicable)
                        """;
                    break;
                case "skills":
                    main = """
                        Evaluate how well the candidate’s skills match the requirements listed in the job description.

                        Consider:
                            - Exact matches of tools, frameworks, and methodologies
                            - Breadth vs. depth of skillset
                            - Level of proficiency if indicated
                        """;
                    break;
                case "languages":
                    main = """
                        Compare the languages (spoken) listed by the candidate to those required or preferred in the job description.

                        Consider:
                            - Required language proficiency
                            - Any certifications or fluency indicators
                        """;
                    break;
            }

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new {
                        role = "system",
                        content = $"{intro}\n{main}\n\n{outro}"
                    },

                    new {
                        role = "user",
                        content = $"Job Description:\n{jobDescription}\n\n{category}:\n" + string.Join("\n\n", cvSection.Select(x => x.ToString()))
                    }
                },
                temperature = 0.7
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error: {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        public async Task<List<float>> GetEmbeddingAsync(string inputText)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                input = inputText,
                model = "text-embedding-3-small"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error (Embeddings): {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var embeddingArray = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToList();

            return embeddingArray;
        }

        public double CosineSimilarity(List<float> v1, List<float> v2)
        {
            if (v1.Count != v2.Count) throw new ArgumentException("Vector lengths must match");

            double dot = 0.0, norm1 = 0.0, norm2 = 0.0;
            for (int i = 0; i < v1.Count; i++)
            {
                dot += v1[i] * v2[i];
                norm1 += Math.Pow(v1[i], 2);
                norm2 += Math.Pow(v2[i], 2);
            }
            return dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        }

    }
}
