stage('Build') {
  node('windows') {
    checkout scm
    powershell '.\\build\\Build-Host.ps1'
    archiveArtifacts 'output/**'
  }
}