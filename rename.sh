#!/bin/bash
set -e

DIR="$(pwd)"

cd src
for dir in soccer-ai-*; do
  if [ -d "$dir" ]; then
    new_dir="${dir//gpt/ai}"
    mv "$dir" "$new_dir"
  fi
done

for dir in soccer-ai-*; do
  if [ -d "$dir" ]; then
    cd "$dir"
    for file in soccer-ai-*.csproj; do
      if [[ -f "$file" ]]; then
        new_file="${file//gpt/ai}"
        mv "$file" "$new_file"
      fi
    done
    cd ..
  fi
done
cd "$DIR"

if [ -f "soccer-ai-api.sln" ]; then mv soccer-ai-api.sln soccer-ai-api.sln; fi
if [ -f "soccer-ai-api.sln.DotSettings.user" ]; then mv soccer-ai-api.sln.DotSettings.user soccer-ai-api.sln.DotSettings.user; fi

find . -name "*.sln" -exec sed -i '' 's/soccer-ai-/soccer-ai-/g' {} +
find . -name "*.sln" -exec sed -i '' 's/SoccerAi./SoccerAi./g' {} +

find src -name "*.csproj" -exec sed -i '' 's/soccer-ai-/soccer-ai-/g' {} +
find src -name "*.csproj" -exec sed -i '' 's/SoccerAi./SoccerAi./g' {} +

find src -name "*.cs" -exec sed -i '' 's/namespace SoccerAi./namespace SoccerAi./g' {} +
find src -name "*.cs" -exec sed -i '' 's/using SoccerAi./using SoccerAi./g' {} +
# Replace remaining 'SoccerAi....' strings in cs files (fully qualified names)
find src -name "*.cs" -exec sed -i '' 's/SoccerAi.api/SoccerAi.Api/g' {} +
find src -name "*.cs" -exec sed -i '' 's/SoccerAi.application/SoccerAi.Application/g' {} +
find src -name "*.cs" -exec sed -i '' 's/SoccerAi.domain/SoccerAi.Domain/g' {} +
find src -name "*.cs" -exec sed -i '' 's/SoccerAi.infrastructure/SoccerAi.Infrastructure/g' {} +
find src -name "*.cs" -exec sed -i '' 's/SoccerAi.worker/SoccerAi.Worker/g' {} +

find . -type f \( -name "Dockerfile*" -o -name "docker-compose.yml" -o -name "*.json" -o -name "*.md" -o -name "*.sh" \) -not -path "*/.git/*" -not -path "*/bin/*" -not -path "*/obj/*" -not -path "*/venv/*" -not -path "*/.venv/*" -exec sed -i '' 's/soccer-ai-/soccer-ai-/g' {} +
find . -type f \( -name "Dockerfile*" -o -name "docker-compose.yml" -o -name "*.json" -o -name "*.md" -o -name "*.sh" \) -not -path "*/.git/*" -not -path "*/bin/*" -not -path "*/obj/*" -not -path "*/venv/*" -not -path "*/.venv/*" -exec sed -i '' 's/SoccerAi./SoccerAi./g' {} +
