import google.generativeai as genai
import os

api_key = os.getenv("GEMINI_API_KEY")
if not api_key:
    # Hardcode for this script since env might not persist in run_command shell
    api_key = "AIzaSyCIJDTc1JWIzVkYmC3XIlBN9Hfx5kYJoGU"

genai.configure(api_key=api_key)

print("Listing models...")
for m in genai.list_models():
    if 'generateContent' in m.supported_generation_methods:
        print(m.name)
