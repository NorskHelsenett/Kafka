echo "Storing sample file"

curl \
   -X POST \
   -H "X-Blob-Name: sample/blob/name.whatevs" \
   --data-binary "@./ExampleFileInput.txt" \
   "http://localhost:8080/register" \
   --silent --show-error \
  >PostFileResponse.txt 2>PostFileError.txt

echo "Retrieving posted file"

curl \
   -X GET \
   -H "X-Blob-Name: sample/blob/name.whatevs" \
   "http://localhost:8080/retrievestream" \
   --silent --show-error \
  >ExampleFileOutput.txt 2>ExampleFileRetrievalError.txt
