stage('Build') {
  node('windows') {
    checkout scm
    powershell '.\\build\\Build-Win32.ps1'
    archiveArtifacts 'output/**'
  }
}