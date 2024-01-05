# FileDeploy

- Deploy files to server with WebApi
- Execute commands on server with WebApi
- Support for DevOps

## Supported systems

- Windows / Windows Server
- Linux
- MacOS

## Params

- ApiKey: Header variable, for verify identity
- path: Form data, where the file save
- preCommand: Form data, execute command before save file
- postCommand: Form data, execute command after save file
- \<filePath\>: Form data, the key is file relative path, the value is file data. Allow multiple.

## Examples

### 1. Deploy dist dir

```sh
set -e

apiKey=$1
apiUrl="http://127.0.0.1:8081"
dir="./dist"

if [ -z "$1" ]; then
  echo "no apiKey!"
  exit 1
fi

function send_file() {
  echo "PUT file: "$dir"/"$1

  status=$(
    curl \
      -s \
      -X PUT \
      -H 'Content-Type:multipart/form-data;charset=utf-8' \
      -H "ApiKey:${apiKey}" \
      -F "path=C:/Web/TestWeb" \
      -F "$1=@${dir}/$1" \
      -w "%{http_code}" \
      --connect-timeout 240 \
      --keepalive-time 240 \
      --retry 50 \
      --retry-max-time 0 \
      --retry-all-errors \
      --compressed \
      $apiUrl
  )

  if [ $status -ne 204 ]; then
    echo "Request failed, status = ${status}, file = $1"
    exit 1
  fi
}

function read_dir() {
  [[ $1 = "" ]] && dirPath=$dir || dirPath="$dir/$1"
  for file in $(ls $dirPath); do
    [[ $1 = "" ]] && filePath=$file || filePath="$1/$file"
    if [ -d $dir"/"$filePath ]; then
      read_dir $filePath
    else
      send_file $filePath
    fi
  done
}

read_dir ""

echo "success"
```

### 2. Stop Website before deploying dir and Run Website after deploying

```sh
set -e

apiKey=$1
apiUrl="http://127.0.0.1:8081"
taskName=SCHEDULE_TASK_NAME
dir="./publish"

if [ -z "$1" ]; then
  echo "no apiKey!"
  exit 1
fi

function send_file() {
  echo "PUT file: "$dir"/"$1

  status=$(
    curl \
      -s \
      -X PUT \
      -H 'Content-Type:multipart/form-data;charset=utf-8' \
      -H "ApiKey:${apiKey}" \
      -F "path=C:/Web/TestWeb" \
      -F "$1=@${dir}/$1" \
      -w "%{http_code}" \
      --connect-timeout 240 \
      --keepalive-time 240 \
      --retry 50 \
      --retry-max-time 0 \
      --retry-all-errors \
      --compressed \
      $apiUrl
  )

  if [ $status -ne 204 ]; then
    echo "Request failed, status = ${status}, file = $1"
    exit 1
  fi
}

function exec_command() {
  echo "EXEC command: $1"

  status=$(
    curl \
      -s \
      -X PUT \
      -H 'Content-Type:multipart/form-data;charset=utf-8' \
      -H "ApiKey:${apiKey}" \
      -F "path=C:/Web/TestWeb" \
      -F "preCommand=\"$1\"" \
      -w "%{http_code}" \
      --connect-timeout 240 \
      --keepalive-time 240 \
      --retry 50 \
      --retry-max-time 0 \
      --retry-all-errors \
      --compressed \
      $apiUrl
  )

  if [ $status -ne 204 ]; then
    echo "Request failed, status = ${status}, file = $1"
    exit 1
  fi
}

function read_dir() {
  [[ $1 = "" ]] && dirPath=$dir || dirPath="$dir/$1"
  for file in $(ls $dirPath); do
    [[ $1 = "" ]] && filePath=$file || filePath="$1/$file"
    if [ -d $dir"/"$filePath ]; then
      read_dir $filePath
    else
      send_file $filePath $file
    fi
  done
}

exec_command "schtasks /end /tn $taskName
  schtasks /change /tn $taskName /disable"
read_dir ""
exec_command "schtasks /change /tn $taskName /enable
  schtasks /run /tn $taskName"

echo "success"
```
