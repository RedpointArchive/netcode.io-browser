stage('Build') {
  node('windows') {
    checkout scm
    powershell '.\\build\\Build-Win32.ps1'
    stash includes: 'netcode.io.demoserver/**', name: 'demoserver'
    archiveArtifacts 'output/**'
  }
}
stage('Build Docker') {
  node('linux-docker') {
    unstash 'demoserver'
    sh 'ls && docker build .'
  }
}