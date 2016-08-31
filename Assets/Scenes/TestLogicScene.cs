﻿using UnityEngine;
using System.Collections.Generic;
using System.Text;

public class Replay
{
	readonly GameFixedUpdate _gameFixedUpdate;

	readonly CommandsRecorder _commandsRecorder;

	readonly RecorderView _recorderView;

	readonly ChecksumRecorder _checksumRecorder;

	readonly CommandsList _commandsList;

	ChecksumValidator _checksumValidator;

	bool _recording;

	int gameFramesPerChecksumCheck = 10;

	int _lastRecordedGameFrame; 

	public int GameFramesPerChecksumCheck {
		get {
			return gameFramesPerChecksumCheck;
		}
		set {
			gameFramesPerChecksumCheck = value;
		}
	}

	public bool IsRecording
	{
		get {
			return _recording;
		}
	}

	public Replay(GameFixedUpdate gameFixedUpdate, ChecksumRecorder checksumRecorder, RecorderView recorderView, CommandsList commandsList)
	{
		_recording = true;
		_lastRecordedGameFrame = 0;
		_commandsRecorder = new CommandsRecorder ();
		_checksumRecorder = checksumRecorder;
		_gameFixedUpdate = gameFixedUpdate;
		_recorderView = recorderView;
		_commandsList = commandsList;
	}

	public void StartPlayback()
	{
		_recording = false;

		_recorderView.StartPlayback ();

		_checksumValidator = new ChecksumValidatorBasic (_checksumRecorder.StoredChecksums);
	}

	public void StartRecording()
	{
		_recording = true;
		_recorderView.StartRecording();

		_checksumRecorder.Reset ();
	}

	public void RecordCommands()
	{
		List<Command> commands = _commandsList.Commands;
		for (int i = 0; i < commands.Count; i++) {
			var command = commands [i];
			_commandsRecorder.AddCommand (_gameFixedUpdate.GameTime, _gameFixedUpdate.CurrentGameFrame, command);
		}
	}

	public bool IsFinished()
	{
		return _lastRecordedGameFrame == _gameFixedUpdate.CurrentGameFrame;
	}

	public void ReplayCommands()
	{
		List<Command> recordedCommands = new List<Command> ();

		_commandsRecorder.GetCommandsForFrame(_gameFixedUpdate.CurrentGameFrame, recordedCommands);

		for (int i = 0; i < recordedCommands.Count; i++) {
			var command = recordedCommands [i];
			_commandsList.AddCommand (command);
		}

		_commandsList.IsReady = true;
	}

	bool IsChecksumFrame(int frame)
	{
		return (frame % gameFramesPerChecksumCheck) == 0;
	}

	public void Update(int frame)
	{
		ChecksumProvider checksumProvider = _checksumRecorder.ChecksumProvider;

		if (IsChecksumFrame(frame)) {
			if (!_recording && _checksumValidator != null) {
				bool validState = _checksumValidator.IsValid (frame, checksumProvider.CalculateChecksum ());
				Debug.Log (string.Format ("State({0}): is {1}", frame, validState ? "valid" : "invalid!"));
			} else {
				_checksumRecorder.RecordState (frame);
			}
		}

		if (_recording) {
			_lastRecordedGameFrame = _gameFixedUpdate.CurrentGameFrame;
		}
	}

}

public class TestLogicScene : MonoBehaviour, GameLogic, GameStateProvider {

	public class MoveCommand : Command
	{
		Unit unit;
		Vector2 destination;

		public MoveCommand(Unit unit, Vector2 destination)
		{
			this.unit = unit;
			this.destination = destination;
		}

		public override void Process ()
		{
			base.Process ();
			unit.MoveTo (destination);
		}
	}



	LockstepFixedUpdate gameFixedUpdate;

	CommandsList commandList = new CommandsList();

	public Unit unit;

	public int fixedTimestepMilliseconds = 100;
	public int gameFramesPerLockstep = 4;

	public Camera camera;

	public FeedbackClick feedbackClick;

	public RecorderView recorderView;

	Replay _replay;

	public int gameFramesPerChecksumCheck = 10;

	#region GameStateProvider implementation

	public string GetGameState ()
	{
		StringBuilder strBuilder = new StringBuilder ();

		strBuilder.Append(gameFixedUpdate.CurrentGameFrame);
		unit.AddState (strBuilder);

		return strBuilder.ToString ();
	}

	#endregion

	void Awake()
	{
		ChecksumRecorder checksumRecorder = new ChecksumRecorder (new GameStateChecksumProvider (this));

		ChecksumRecorderDebug checksumRecorderDebug = gameObject.AddComponent<ChecksumRecorderDebug> ();
		checksumRecorderDebug.checksumRecorder = checksumRecorder;

		// TODO: set replay....

		gameFixedUpdate = new LockstepFixedUpdate (new CommandsListLockstepLogic(commandList));
		gameFixedUpdate.GameFramesPerLockstep = gameFramesPerLockstep;
		gameFixedUpdate.FixedStepTime = fixedTimestepMilliseconds / 1000.0f;

		gameFixedUpdate.Init ();
		gameFixedUpdate.SetGameLogic (this);

		_replay = new Replay (gameFixedUpdate, checksumRecorder, recorderView, commandList);
		_replay.GameFramesPerChecksumCheck = gameFramesPerChecksumCheck;

		StartRecording ();

		// debug...
		GameFixedUpdateDebug updateDebug = gameObject.AddComponent<GameFixedUpdateDebug> ();
		updateDebug.SetGameFixedUpdate (gameFixedUpdate);

		Application.targetFrameRate = 60;
	}

	void ResetGameState()
	{
		gameFixedUpdate.Init ();
		unit.SetPosition (new Vector2 (0, 0));
	}

	void StartRecording()
	{
		_replay.StartRecording ();

		ChecksumRecorderDebug checksumRecorderDebug = gameObject.GetComponent<ChecksumRecorderDebug> ();
		checksumRecorderDebug.Reset ();
	}
	
	// Update is called once per frame
	void Update () {

		gameFixedUpdate.GameFramesPerLockstep = gameFramesPerLockstep;
		gameFixedUpdate.FixedStepTime = fixedTimestepMilliseconds / 1000.0f;
	
		if (Input.GetKeyUp (KeyCode.P)) {

			ResetGameState ();
			_replay.StartPlayback ();

			return;
		}

		if (Input.GetKeyUp (KeyCode.R)) {

			// resets game fixed update state...
			StartRecording();

			return;
		}

		if (_replay.IsRecording) {

			gameFixedUpdate.Update (Time.deltaTime);

			if (Input.GetMouseButtonUp (1)) {
				Vector2 position = camera.ScreenToWorldPoint (Input.mousePosition);
				var moveCommand = new MoveCommand (unit, position);

				commandList.AddCommand (moveCommand);
				feedbackClick.ShowFeedback (position);

				_replay.RecordCommands ();

//				_commandsRecorder.AddCommand (gameFixedUpdate.GameTime, gameFixedUpdate.CurrentGameFrame, moveCommand);
			}

			if (Input.touchCount > 0) {
		
				if (Input.GetTouch (0).phase == TouchPhase.Ended) {
					Vector2 position = camera.ScreenToWorldPoint (Input.GetTouch (0).position);
					var moveCommand = new MoveCommand (unit, position);
					commandList.AddCommand (moveCommand);			
					feedbackClick.ShowFeedback (position);

					_replay.RecordCommands ();

//					_commandsRecorder.AddCommand (gameFixedUpdate.GameTime, gameFixedUpdate.CurrentGameFrame, moveCommand);
				}

			}

			commandList.IsReady = true;
		} else {
		
			// playback...

			// if already at last frame, then dont update anymore...
			if (_replay.IsFinished())
				return;

			gameFixedUpdate.Update (Time.deltaTime);

			_replay.ReplayCommands ();
		}
	}

	#region DeterministicGameLogic implementation

	public void Update (float dt, int frame)
	{
		// Debug.Log ("Timestep: " + frame);

		_replay.Update (frame);

		// update game state...
		unit.GameUpdate (dt, frame);
	}

	#endregion
}
