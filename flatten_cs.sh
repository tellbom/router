mkdir -p temp

find . -type f -name "*.cs" ! -path "./temp/*" | while read file
do
  newname=$(echo "$file" | sed 's|^\./||' | sed 's|/|_|g')
  cp "$file" "temp/$newname"
done