using AppWrap.Build;
var root=Path.Combine(AppContext.BaseDirectory,"evidence",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var prepared=new PreparedArtifact[2];var original=new OutputIdentity[2];
for(var i=0;i<2;i++){var source=Path.Combine(root,$"prepared{i}.exe");var dest=Path.Combine(root,$"target{i}.exe");File.WriteAllText(source,$"new{i}");File.WriteAllText(dest,$"old{i}");prepared[i]=new(source,dest,OutputIdentity.HashFile(source),new FileInfo(source).Length);original[i]=OutputIdentity.Capture(dest);}
try{await BatchArtifactPublisher.PublishAsync(prepared,original,CancellationToken.None,i=>{if(i==1)throw new IOException("owned injected second-commit failure");});Console.Error.WriteLine("FAIL: injected failure unexpectedly succeeded");return 1;}catch(BuildFailureException){}
for(var i=0;i<2;i++){if(File.ReadAllText(prepared[i].Destination)!=$"old{i}"){Console.Error.WriteLine("FAIL: original destination not restored byte-for-byte");return 1;}}
Console.WriteLine("PASS: second commit failure restores both original destinations byte-for-byte");return 0;
