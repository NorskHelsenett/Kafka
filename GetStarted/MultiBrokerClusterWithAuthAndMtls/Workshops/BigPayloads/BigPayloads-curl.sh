echo "Storing sample file"

curl \
   -X POST \
   -H "X-Blob-Name: sample/blob/ramen.ipsum" \
   --data-binary "@./ExampleFileInput.txt" \
   "http://localhost:8080/store" \
   --silent --show-error \
  >PostFileResponse.txt 2>PostFileError.txt

echo "Retrieving posted file"

curl \
   -X GET \
   -H "X-Blob-Name: sample/blob/ramen.ipsum" \
   "http://localhost:8080/retrieve" \
   --silent --show-error \
  >ExampleFileOutput.txt 2>ExampleFileRetrievalError.txt
